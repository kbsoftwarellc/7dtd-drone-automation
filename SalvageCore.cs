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

        /// Q1 reach as a fraction of the configured (Q6) reach; Q6 = full.
        public float LowQualityReach = 0.55f;
        /// Q1 action time as a multiple of the configured (Q6) time; Q6 = full speed.
        public float LowQualityTimeMult = 2f;

        public void Clamp()
        {
            if (Radius < 0f) Radius = 0f;
            if (VerticalRadius < 0f) VerticalRadius = 0f;
            if (SecondsPerStep < 0.05f) SecondsPerStep = 0.05f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
            QualityScale.ClampKnobs(ref LowQualityReach, ref LowQualityTimeMult);
        }
    }

    /// <summary>
    /// Auto-Salvage: wrenches nearby salvageable blocks (cars, sinks, working machines) one
    /// downgrade step per action, depositing the salvage into the drone bag.
    ///
    /// Destructive, so it is deliberately conservative: it only touches blocks whose Harvest drops
    /// are tagged as salvage (which excludes terrain, ore, wood, plants and normal loot), only on
    /// UNCLAIMED ground - never inside anyone's land claim, so it cannot wreck your base or a
    /// neighbour's - and never a container that still holds loot the player hasn't collected. One
    /// downgrade step per tick means a car visibly comes apart over several seconds.
    ///
    /// The work bubble is centred on the OWNER, not the drone: the drone hovers off to your side
    /// and drifts, so anchoring to the player makes it reliably salvage what you're standing next to.
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

        public bool Tick(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData, EntityDrone _drone, int _quality, DroneBoost _boost)
        {
            pacer.Accrue();
            if (settings.Radius <= 0f) return false;

            float radius = QualityScale.Reach(settings.Radius, settings.LowQualityReach, _quality) * _boost.ReachMult;
            float vertical = QualityScale.Reach(settings.VerticalRadius, settings.LowQualityReach, _quality) * _boost.ReachMult;
            float secondsPerStep = QualityScale.Time(settings.SecondsPerStep, settings.LowQualityTimeMult, _quality) * _boost.SpeedMult;

            if (pacer.Credit < secondsPerStep) return false;

            // Scan around the OWNER, not the drone: the drone hovers a few metres off you and
            // drifts, so a player-anchored bubble reliably covers what you're standing next to.
            DroneWorld.CollectParents(_world, _owner.position, radius, vertical, buffer);

            int did = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (pacer.Credit < secondsPerStep) break;

                Vector3i pos = buffer[i];
                BlockValue bv = _world.GetBlock(pos);
                if (bv.isair) continue;

                Block b = bv.Block;
                if (b == null || !IsSalvageable(b)) continue;

                // Unclaimed ground only - protects your own base and everyone else's.
                if (DroneWorld.Claim(_world, _ownerData, pos) != EnumLandClaimOwner.None) continue;

                // Never wrench a container that still holds loot the player hasn't taken. Many
                // world containers generate their loot only on first open, so an untouched one
                // can read empty yet still pay out - leave anything untouched, non-empty, or
                // player-owned for the player, and only salvage a container once it's emptied.
                if (HoldsUnlootedContents(_world, pos)) continue;

                if (!pacer.TrySpend(secondsPerStep)) break;

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
        /// True when the block at <paramref name="_pos"/> is a loot container that still has loot
        /// for the player: untouched (world loot often only generates on first open, so it can read
        /// empty yet still pay out), touched-but-non-empty (player left items behind), or player-
        /// owned storage. Blocks with no lootable tile entity (plain wrecks, sinks) return false and
        /// salvage normally. Prevents the drone destroying loot the player has not collected.
        /// </summary>
        private static bool HoldsUnlootedContents(World _world, Vector3i _pos)
        {
            TileEntity te = _world.GetTileEntity(_pos);
            if (te == null) return false;
            if (!te.TryGetSelfOrFeature(out ITileEntityLootable loot)) return false;

            if (loot.bPlayerStorage) return true;
            if (!loot.bTouched) return true;
            if (!loot.IsEmpty()) return true;
            return false;
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
