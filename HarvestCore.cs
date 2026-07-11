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

        public HarvestCore(HarvestSettings _settings)
        {
            settings = _settings;
            pacer = new Pacer(_settings.MaxCatchupSeconds);
        }

        public bool Tick(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData, EntityDrone _drone, int _quality)
        {
            pacer.Accrue();
            if (settings.Radius <= 0f) return false;

            float radius = QualityScale.Reach(settings.Radius, settings.LowQualityReach, _quality);
            float vertical = QualityScale.Reach(settings.VerticalRadius, settings.LowQualityReach, _quality);
            float secondsPerTarget = QualityScale.Time(settings.SecondsPerTarget, settings.LowQualityTimeMult, _quality);

            if (pacer.Credit < secondsPerTarget) return false;

            DroneWorld.CollectParents(_world, _drone.position, radius, vertical, buffer);

            int did = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (pacer.Credit < secondsPerTarget) break;

                Vector3i pos = buffer[i];
                BlockValue bv = _world.GetBlock(pos);
                if (bv.isair) continue;

                Block b = bv.Block;
                if (b == null || !IsGrownCrop(b)) continue;

                // Your own farm only.
                if (DroneWorld.Claim(_world, _ownerData, pos) != EnumLandClaimOwner.Self) continue;

                // Must know what to replant, or we leave the crop alone rather than destroy it.
                if (!TryGetReplant(b, out BlockValue young)) continue;

                if (!pacer.TrySpend(secondsPerTarget)) break;

                DroneWorld.EmitDrops(b, EnumDropEvent.Harvest, bv, _owner, _drone, rand);

                // Replanting the young stage re-arms its growth schedule (BlockPlantGrowing.OnBlockAdded)
                // and syncs to clients; it also stops this crop being reaped again until it regrows.
                _world.SetBlockRPC(pos, young);
                did++;
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
        /// to a growing plant block (e.g. corn drops plantedCorn1 alongside its food). Food drops
        /// resolve to no block, so they are ignored here.
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
