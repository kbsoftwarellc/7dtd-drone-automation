using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>Tunables for Auto-Harvest, read from mod/droneautomation.xml.</summary>
    public sealed class HarvestSettings
    {
        public float Radius = 6f;
        public float VerticalRadius = 4f;

        /// Seconds charged per crop harvested.
        public float SecondsPerTarget = 1f;
        public float MaxCatchupSeconds = 5f;

        /// Q1 reach as a fraction of the configured (Q6) reach; Q6 = full.
        public float LowQualityReach = 0.55f;
        /// Q1 action time as a multiple of the configured (Q6) time; Q6 = full speed.
        public float LowQualityTimeMult = 2f;

        public void Clamp()
        {
            if (Radius < 0f) Radius = 0f;
            if (VerticalRadius < 0f) VerticalRadius = 0f;
            if (SecondsPerTarget < 0.05f) SecondsPerTarget = 0.05f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
            QualityScale.ClampKnobs(ref LowQualityReach, ref LowQualityTimeMult);
        }
    }

    /// <summary>
    /// Auto-Harvest: reaps grown crops in your own land claim and replants them, into the drone bag.
    ///
    /// It only acts on your OWN claim (never a neighbour's farm), and only on the grown stage - the
    /// growing stages are BlockPlantGrowing and carry no Harvest drops, so they are skipped. A crop
    /// is only reaped if its replant (young) stage can be resolved from its own drops; otherwise it
    /// is left untouched, so a detection miss can never destroy a plot. Replanting is also what stops
    /// the same crop being reaped every pass.
    /// </summary>
    public sealed class HarvestCore
    {
        private readonly HarvestSettings settings;
        private readonly Pacer pacer;
        private readonly System.Random rand = new System.Random();

        private static readonly List<Vector3i> buffer = new List<Vector3i>();
        private static readonly FastTags<TagGroup.Global> cropsTag = FastTags<TagGroup.Global>.Parse("crops");

        /// <summary>
        /// Why the last scan reaped nothing, for the Debug log. "Nothing in range" is not a useful
        /// thing to tell someone standing in a field of corn: the interesting number is WHICH of the
        /// four conditions threw the crop out - out of reach, not a crop, not your claim, or no
        /// replant - because each one points at a different fix.
        /// </summary>
        public string LastScan { get; private set; }

        public HarvestCore(HarvestSettings _settings)
        {
            settings = _settings;
            pacer = new Pacer(_settings.MaxCatchupSeconds);
        }

        public bool Tick(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData, EntityDrone _drone, Vector3 _scanCenter, int _quality, DroneBoost _boost)
        {
            pacer.Accrue();
            LastScan = null;
            if (settings.Radius <= 0f) { LastScan = "Radius is 0 in config"; return false; }

            float radius = QualityScale.Reach(settings.Radius, settings.LowQualityReach, _quality) * _boost.ReachMult;
            float vertical = QualityScale.Reach(settings.VerticalRadius, settings.LowQualityReach, _quality) * _boost.ReachMult;
            float secondsPerTarget = QualityScale.Time(settings.SecondsPerTarget, settings.LowQualityTimeMult, _quality) * _boost.SpeedMult;

            if (pacer.Credit < secondsPerTarget)
            {
                LastScan = $"banking time ({pacer.Credit:0.0}/{secondsPerTarget:0.0}s)";
                return false;
            }

            // The caller picks the anchor: where the drone is parked when it's holding
            // position, otherwise the owner (the drone drifts as it hovers beside you).
            DroneWorld.CollectParents(_world, _scanCenter, radius, vertical, buffer);

            int did = 0, crops = 0, outsideClaim = 0, noReplant = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (pacer.Credit < secondsPerTarget) break;

                Vector3i pos = buffer[i];
                BlockValue bv = _world.GetBlock(pos);
                if (bv.isair) continue;

                Block b = bv.Block;
                if (b == null || !IsGrownCrop(b)) continue;
                crops++;

                // Your own farm only.
                if (DroneWorld.Claim(_world, _ownerData, pos) != EnumLandClaimOwner.Self) { outsideClaim++; continue; }

                // Must know what to replant, or we leave the crop alone rather than destroy it.
                if (!TryGetReplant(b, out BlockValue young)) { noReplant++; continue; }

                if (!pacer.TrySpend(secondsPerTarget)) break;

                DroneWorld.EmitDrops(b, EnumDropEvent.Harvest, bv, _owner, _drone, rand);

                // Replanting the young stage re-arms its growth schedule (BlockPlantGrowing.OnBlockAdded)
                // and syncs to clients; it also stops this crop being reaped again until it regrows.
                _world.SetBlockRPC(pos, young);
                did++;
            }

            if (did == 0)
            {
                LastScan = $"anchor ({_scanCenter.x:0},{_scanCenter.y:0},{_scanCenter.z:0}) r={radius:0.0}/{vertical:0.0} q{_quality}: "
                         + $"{buffer.Count} blocks, {crops} grown crops"
                         + (crops == 0 ? " -> none in reach" : $", {outsideClaim} outside your claim, {noReplant} with no replant");
            }

            return did > 0;
        }

        /// <summary>
        /// Grown crop = carries the crops tag and has Harvest drops. The growing stages are
        /// BlockPlantGrowing with no Harvest drops, so this cleanly picks only the harvestable stage.
        /// </summary>
        private static bool IsGrownCrop(Block _b)
        {
            if (_b is BlockPlantGrowing) return false;
            if (!_b.HasAnyFastTags(cropsTag)) return false;
            return _b.HasItemsToDropForEvent(EnumDropEvent.Harvest);
        }

        /// <summary>
        /// Finds the replant block among the crop's own Harvest drops: the drop whose item resolves
        /// to a growing plant block. Food drops resolve to no block, so they are ignored here.
        ///
        /// WHICH crop block this is matters, and the distinction is easy to miss. Every crop has TWO
        /// harvestable variants: the WILD one that generates in the world (`plantedCorn3Harvest`,
        /// produce drops only) and the PLAYER-grown one a farm plot actually grows into
        /// (`plantedCorn3HarvestPlayer`), which additionally lists its young stage:
        ///
        ///     &lt;drop event="Harvest" name="plantedCorn1" count="1" tag="farmerBonusSeed" /&gt;
        ///
        /// Only the Player variant is on the growth chain - `plantedCorn1 -&gt; plantedCorn2 -&gt;
        /// plantedCorn3HarvestPlayer` - so only it is what the module ever meets on a real farm.
        /// A wild crop therefore finds no replant and is deliberately left standing rather than
        /// reaped into a bare plot. If you are testing this by hand, spawn the *Player* variant or
        /// grow one from `plantedCorn1`; spawning `plantedCorn3Harvest` reproduces a "does nothing"
        /// that is the module behaving correctly.
        /// </summary>
        private static bool TryGetReplant(Block _b, out BlockValue _young)
        {
            _young = default;
            if (_b.itemsToDrop == null) return false;
            if (!_b.itemsToDrop.TryGetValue(EnumDropEvent.Harvest, out List<Block.SItemDropProb> list) || list == null) return false;

            for (int i = 0; i < list.Count; i++)
            {
                string name = list[i].name;
                if (string.IsNullOrEmpty(name) || name == "*") continue;

                ItemValue iv = ItemClass.GetItem(name);
                if (iv == null || iv.IsEmpty()) continue;

                BlockValue candidate = iv.ToBlockValue();
                if (!candidate.isair && candidate.Block is BlockPlantGrowing)
                {
                    _young = candidate;
                    return true;
                }
            }
            return false;
        }
    }
}
