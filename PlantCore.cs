using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>Tunables for Auto-Plant, read from mod/droneautomation.xml.</summary>
    public sealed class PlantSettings
    {
        public float Radius = 6f;
        public float VerticalRadius = 3f;

        /// Seconds charged per crop planted.
        public float SecondsPerPlant = 1f;
        public float MaxCatchupSeconds = 5f;

        /// Q1 reach as a fraction of the configured (Q6) reach; Q6 = full.
        public float LowQualityReach = 0.55f;
        /// Q1 action time as a multiple of the configured (Q6) time; Q6 = full speed.
        public float LowQualityTimeMult = 2f;

        public void Clamp()
        {
            if (Radius < 0f) Radius = 0f;
            if (VerticalRadius < 0f) VerticalRadius = 0f;
            if (SecondsPerPlant < 0.05f) SecondsPerPlant = 0.05f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
            QualityScale.ClampKnobs(ref LowQualityReach, ref LowQualityTimeMult);
        }
    }

    /// <summary>
    /// Auto-Plant: sows young crops from the drone bag onto empty farm plots in your own land claim.
    ///
    /// A "plantable" bag item is any that resolves to a BlockPlantGrowing (e.g. plantedCorn1) - the
    /// very block Auto-Harvest deposits when it reaps, so the two modules close a loop: harvest fills
    /// the bag with seed blocks, plant spends them refilling empty plots. It only ever plants on the
    /// air cell above a farm-plot block inside your claim, so it can never sow on invalid ground, on
    /// a neighbour's farm, or on top of a crop that is already growing.
    /// </summary>
    public sealed class PlantCore
    {
        private readonly PlantSettings settings;
        private readonly Pacer pacer;

        private static readonly List<Vector3i> buffer = new List<Vector3i>();
        private readonly List<Plantable> plantables = new List<Plantable>();

        private struct Plantable { public ItemValue item; public BlockValue young; }

        public PlantCore(PlantSettings _settings)
        {
            settings = _settings;
            pacer = new Pacer(_settings.MaxCatchupSeconds);
        }

        public bool Tick(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData, EntityDrone _drone, Vector3 _scanCenter, int _quality, DroneBoost _boost)
        {
            pacer.Accrue();
            if (settings.Radius <= 0f) return false;

            float radius = QualityScale.Reach(settings.Radius, settings.LowQualityReach, _quality) * _boost.ReachMult;
            float vertical = QualityScale.Reach(settings.VerticalRadius, settings.LowQualityReach, _quality) * _boost.ReachMult;
            float secondsPerPlant = QualityScale.Time(settings.SecondsPerPlant, settings.LowQualityTimeMult, _quality) * _boost.SpeedMult;

            if (pacer.Credit < secondsPerPlant) return false;

            // Nothing to sow with? Bail before the scan.
            CollectPlantables(_drone.bag);
            if (plantables.Count == 0) return false;

            // The caller picks the anchor: where the drone is parked when it's holding
            // position, otherwise the owner (the drone drifts as it hovers beside you).
            DroneWorld.CollectParents(_world, _scanCenter, radius, vertical, buffer);

            int did = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (pacer.Credit < secondsPerPlant) break;

                Vector3i pos = buffer[i];
                BlockValue bv = _world.GetBlock(pos);
                if (bv.isair) continue;

                Block b = bv.Block;
                if (b == null || !IsFarmPlot(b)) continue;

                // The crop grows in the cell above the plot; only sow an empty one.
                Vector3i above = new Vector3i(pos.x, pos.y + 1, pos.z);
                if (!_world.GetBlock(above).isair) continue;

                // Your own claim only.
                if (DroneWorld.Claim(_world, _ownerData, pos) != EnumLandClaimOwner.Self) continue;

                int pick = NextAvailable(_drone.bag);
                if (pick < 0) break; // bag exhausted this pass

                if (!pacer.TrySpend(secondsPerPlant)) break;

                // SetBlockRPC arms the plant's growth schedule (BlockPlantGrowing.OnBlockAdded) and
                // syncs the change to clients, exactly like Auto-Harvest's replant.
                _world.SetBlockRPC(above, plantables[pick].young);
                _drone.bag.DecItem(plantables[pick].item, 1);
                did++;
            }

            plantables.Clear();
            return did > 0;
        }

        /// <summary>Farm plots (all variants: raised, player, corner, ...) are named "farmPlot*".</summary>
        private static bool IsFarmPlot(Block _b)
        {
            string name = _b.GetBlockName();
            return !string.IsNullOrEmpty(name) && name.StartsWith("farmPlot", System.StringComparison.Ordinal);
        }

        /// <summary>Distinct young-crop block items currently in the bag.</summary>
        private void CollectPlantables(Bag _bag)
        {
            plantables.Clear();

            ItemStack[] slots = _bag.GetSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                ItemStack stack = slots[i];
                if (stack == null || stack.IsEmpty()) continue;

                ItemValue iv = stack.itemValue;
                if (iv == null || iv.IsEmpty()) continue;
                if (AlreadyListed(iv)) continue;

                BlockValue candidate = iv.ToBlockValue();
                if (candidate.isair || !(candidate.Block is BlockPlantGrowing)) continue;

                plantables.Add(new Plantable { item = iv, young = candidate });
            }
        }

        private bool AlreadyListed(ItemValue _iv)
        {
            for (int i = 0; i < plantables.Count; i++)
                if (plantables[i].item.type == _iv.type) return true;
            return false;
        }

        /// <summary>Index of the first plantable the bag still holds at least one of.</summary>
        private int NextAvailable(Bag _bag)
        {
            for (int i = 0; i < plantables.Count; i++)
                if (_bag.GetItemCount(plantables[i].item) > 0) return i;
            return -1;
        }
    }
}
