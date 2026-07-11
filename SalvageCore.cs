using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>Tunables for Auto-Salvage, read from mod/droneautomation.xml.</summary>
    public sealed class SalvageSettings
    {
        /// Modest by default: this destroys blocks, so a small reach avoids surprises.
        public float Radius = 4f;
        public float VerticalRadius = 4f;

        /// Seconds charged per downgrade step (one "wrench swing").
        public float SecondsPerStep = 1.5f;
        public float MaxCatchupSeconds = 5f;

        public void Clamp()
        {
            if (Radius < 0f) Radius = 0f;
            if (VerticalRadius < 0f) VerticalRadius = 0f;
            if (SecondsPerStep < 0.05f) SecondsPerStep = 0.05f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
        }
    }

    /// <summary>
    /// Auto-Salvage: wrenches nearby salvageable blocks (cars, sinks, working machines) one
    /// downgrade step per action, depositing the salvage into the drone bag.
    ///
    /// Destructive, so it is deliberately conservative: it only touches blocks whose Harvest drops
    /// are tagged as salvage (which excludes terrain, ore, wood, plants and normal loot), and only
    /// on UNCLAIMED ground - never inside anyone's land claim, so it cannot wreck your base or a
    /// neighbour's. One downgrade step per tick means a car visibly comes apart over several seconds.
    /// </summary>
    public sealed class SalvageCore
    {
        private readonly SalvageSettings settings;
        private readonly Pacer pacer;
        private readonly System.Random rand = new System.Random();

        private static readonly List<Vector3i> buffer = new List<Vector3i>();

        public SalvageCore(SalvageSettings _settings)
        {
            settings = _settings;
            pacer = new Pacer(_settings.MaxCatchupSeconds);
        }

        public bool Tick(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData, EntityDrone _drone)
        {
            pacer.Accrue();
            if (settings.Radius <= 0f) return false;
            if (pacer.Credit < settings.SecondsPerStep) return false;

            DroneWorld.CollectParents(_world, _drone.position, settings.Radius, settings.VerticalRadius, buffer);

            int did = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (pacer.Credit < settings.SecondsPerStep) break;

                Vector3i pos = buffer[i];
                BlockValue bv = _world.GetBlock(pos);
                if (bv.isair) continue;

                Block b = bv.Block;
                if (b == null || !IsSalvageable(b)) continue;

                // Unclaimed ground only - protects your own base and everyone else's.
                if (DroneWorld.Claim(_world, _ownerData, pos) != EnumLandClaimOwner.None) continue;

                if (!pacer.TrySpend(settings.SecondsPerStep)) break;

                // Yield this stage's salvage, then knock it down one downgrade step. The downgraded
                // stage is picked up on a later pass, so a car comes apart stage by stage, exactly
                // like wrenching it by hand.
                DroneWorld.EmitDrops(b, EnumDropEvent.Harvest, bv, _owner, _drone, rand);
                b.DamageBlock(_world, pos, bv, b.MaxDamage, _drone.entityId, null, _bUseHarvestTool: true);
                did++;
            }

            return did > 0;
        }

        /// <summary>
        /// Salvageable = has a Harvest-event drop tagged as salvage. That tag is what marks
        /// wrenchable objects (cars, sinks, safes, machines) apart from terrain, ore and plants,
        /// which use other drop events/tags.
        /// </summary>
        private static bool IsSalvageable(Block _b)
        {
            if (_b.itemsToDrop == null) return false;
            if (!_b.itemsToDrop.TryGetValue(EnumDropEvent.Harvest, out List<Block.SItemDropProb> list) || list == null) return false;

            for (int i = 0; i < list.Count; i++)
            {
                string tag = list[i].tag;
                if (!string.IsNullOrEmpty(tag) && tag.Contains("salvage")) return true;
            }
            return false;
        }
    }
}
