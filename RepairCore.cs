using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>Tunables for Auto-Repair, read from mod/droneautomation.xml.</summary>
    public sealed class RepairSettings
    {
        public float Radius = 6f;
        public float VerticalRadius = 5f;

        /// Seconds charged per block fully repaired.
        public float SecondsPerBlock = 1f;
        public float MaxCatchupSeconds = 5f;

        /// Q1 reach as a fraction of the configured (Q6) reach; Q6 = full.
        public float LowQualityReach = 0.55f;
        /// Q1 action time as a multiple of the configured (Q6) time; Q6 = full speed.
        public float LowQualityTimeMult = 2f;

        public void Clamp()
        {
            if (Radius < 0f) Radius = 0f;
            if (VerticalRadius < 0f) VerticalRadius = 0f;
            if (SecondsPerBlock < 0.05f) SecondsPerBlock = 0.05f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
            QualityScale.ClampKnobs(ref LowQualityReach, ref LowQualityTimeMult);
        }
    }

    /// <summary>
    /// Auto-Repair: repairs damaged blocks in your own land claim, paying the materials out of the
    /// drone's own bag.
    ///
    /// Scoped to your claim only, so it never spends your mats fixing random world blocks or a
    /// neighbour's base. A block is only repaired if the bag holds every material it needs; the cost
    /// mirrors the vanilla repair-tool formula (RepairItems scaled by how damaged the block is and
    /// its ResourceScale). Block.DamageBlock with a negative amount does the repair and the client
    /// sync in one call.
    /// </summary>
    public sealed class RepairCore
    {
        private readonly RepairSettings settings;
        private readonly Pacer pacer;

        private static readonly List<Vector3i> buffer = new List<Vector3i>();

        public RepairCore(RepairSettings _settings)
        {
            settings = _settings;
            pacer = new Pacer(_settings.MaxCatchupSeconds);
        }

        public bool Tick(World _world, PersistentPlayerData _ownerData, EntityDrone _drone, int _quality)
        {
            pacer.Accrue();
            if (settings.Radius <= 0f) return false;

            float radius = QualityScale.Reach(settings.Radius, settings.LowQualityReach, _quality);
            float vertical = QualityScale.Reach(settings.VerticalRadius, settings.LowQualityReach, _quality);
            float secondsPerBlock = QualityScale.Time(settings.SecondsPerBlock, settings.LowQualityTimeMult, _quality);

            if (pacer.Credit < secondsPerBlock) return false;

            DroneWorld.CollectParents(_world, _drone.position, radius, vertical, buffer);

            int did = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (pacer.Credit < secondsPerBlock) break;

                Vector3i pos = buffer[i];
                BlockValue bv = _world.GetBlock(pos);
                if (bv.isair || bv.damage <= 0) continue;

                Block b = bv.Block;
                if (b == null) continue;

                List<Block.SItemNameCount> repairItems = b.RepairItems;
                if (repairItems == null || repairItems.Count == 0) continue;

                // Your own claim only.
                if (DroneWorld.Claim(_world, _ownerData, pos) != EnumLandClaimOwner.Self) continue;

                // Only repair if the bag can pay in full - never leave a block part-repaired.
                if (!CanAfford(b, bv, _drone.bag, out List<Cost> costs)) continue;

                if (!pacer.TrySpend(secondsPerBlock)) break;

                for (int c = 0; c < costs.Count; c++) _drone.bag.DecItem(costs[c].item, costs[c].count);

                // Negative damage repairs; DamageBlock does the block-change RPC to clients itself.
                b.DamageBlock(_world, pos, bv, -bv.damage, _drone.entityId);
                did++;
            }

            return did > 0;
        }

        private struct Cost { public ItemValue item; public int count; }

        private static readonly List<Cost> costBuffer = new List<Cost>();

        /// <summary>
        /// Vanilla repair cost: for each RepairItems entry, count = max(1, entryCount * damageFraction
        /// * ResourceScale). Returns false unless every entry is fully covered by the bag.
        /// </summary>
        private static bool CanAfford(Block _b, BlockValue _bv, Bag _bag, out List<Cost> _costs)
        {
            costBuffer.Clear();
            _costs = costBuffer;

            int maxDamage = Mathf.Max(1, _b.MaxDamage);
            float frac = (float)_bv.damage / maxDamage;
            float scale = _b.ResourceScale;

            List<Block.SItemNameCount> repairItems = _b.RepairItems;
            for (int i = 0; i < repairItems.Count; i++)
            {
                ItemValue iv = ItemClass.GetItem(repairItems[i].ItemName);
                if (iv == null || iv.IsEmpty()) return false;

                int need = Mathf.Max(1, (int)(repairItems[i].Count * frac * scale));
                if (_bag.GetItemCount(iv) < need) return false;

                costBuffer.Add(new Cost { item = iv, count = need });
            }
            return costBuffer.Count > 0;
        }
    }
}
