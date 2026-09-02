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

        /// Off by default: a forge or workbench is something the player uses, not scrap - and it may
        /// still hold their materials. Covers every workstation, POI-placed or player-placed.
        public bool SalvageWorkstations = false;

        /// On by default: wrenching cars and sinks while you clear a building is the module's whole
        /// point. Server ops who want POIs left intact can turn it off.
        public bool SalvageInPOIs = true;

        /// Off by default: a switch, button, relay or pressure plate is what makes a POI work, and
        /// wrenching one leaves a door that can never be opened again. See SalvageGuards.
        public bool SalvageSwitches = false;

        /// Off by default: a working vending machine is one you (and everyone else on the server)
        /// can still buy from, and one you can rent and stock yourself. Its broken shells stay
        /// salvageable either way. See SalvageGuards.IsWorkingVendingMachine.
        public bool SalvageVendingMachines = false;

        /// Heat (screamer) generated per broken stage, as a multiple of what breaking that stage by
        /// hand would make. 1 = parity with wrenching it yourself, 0 = silent as before.
        public float HeatMultiplier = 1f;

        /// On by default: count the drone as holding a salvage tool while it works out what a block
        /// pays, so the bonuses vanilla gates on holding one - Hacker's candy, the Scavenger Gloves -
        /// reach it like they reach you. See SalvageToolContext.
        public bool CountAsHoldingSalvageTool = true;

        /// The tags that pretence claims. Only worth changing under an overhaul that renames them.
        public string SalvageToolTags = SalvageToolContext.DefaultToolTags;

        /// Block names the drone must never wrench, from &lt;exclude block="..."/&gt; in the config.
        public readonly HashSet<string> ExcludedBlocks = new HashSet<string>();

        public void Clamp()
        {
            if (Radius < 0f) Radius = 0f;
            if (VerticalRadius < 0f) VerticalRadius = 0f;
            if (SecondsPerStep < 0.05f) SecondsPerStep = 0.05f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
            if (HeatMultiplier < 0f) HeatMultiplier = 0f;
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
    /// neighbour's - never a switch, button or relay a POI needs to work, and never a container that
    /// still holds loot the player hasn't collected. One downgrade step per tick means a car visibly
    /// comes apart over several seconds.
    ///
    /// A land claim alone is NOT enough protection, which is why the guards below exist. The game's
    /// World.GetLandClaimOwner returns EnumLandClaimOwner.None for a trader area - the very value
    /// this module reads as "safe to wrench" - so the claim check does not merely fail to protect a
    /// trader, it green-lights one. Trader areas are therefore rejected explicitly, and workstations
    /// (a forge or workbench is something you use, and may still hold your materials) are skipped by
    /// default wherever they stand.
    ///
    /// The work bubble is centred on whatever the caller passes as the scan centre: the parked spot
    /// when the drone is holding position, otherwise the OWNER rather than the drone itself (the
    /// drone hovers off to your side and drifts, so a player-anchored bubble reliably covers what
    /// you're standing next to).
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

        public bool Tick(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData, EntityDrone _drone, Vector3 _scanCenter, int _quality, DroneBoost _boost)
        {
            pacer.Accrue();
            if (settings.Radius <= 0f) return false;

            float radius = QualityScale.Reach(settings.Radius, settings.LowQualityReach, _quality) * _boost.ReachMult;
            float vertical = QualityScale.Reach(settings.VerticalRadius, settings.LowQualityReach, _quality) * _boost.ReachMult;
            float secondsPerStep = QualityScale.Time(settings.SecondsPerStep, settings.LowQualityTimeMult, _quality) * _boost.SpeedMult;

            if (pacer.Credit < secondsPerStep) return false;

            // The caller picks the anchor: where the drone is parked when it's holding
            // position, otherwise the owner (the drone drifts as it hovers beside you).
            DroneWorld.CollectParents(_world, _scanCenter, radius, vertical, buffer);

            // Collect the land claims that reach into this bubble once, before walking the blocks.
            SalvageGuards.BeginTick(_scanCenter, radius);

            int did = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (pacer.Credit < secondsPerStep) break;

                Vector3i pos = buffer[i];
                BlockValue bv = _world.GetBlock(pos);
                if (bv.isair) continue;

                Block b = bv.Block;
                if (b == null || !IsSalvageable(b)) continue;

                // Unclaimed ground only - protects your own base and everyone else's. Asked twice on
                // purpose: the vanilla call is the one that knows about allies and game modes, and
                // the claim-map scan is the one that still says "claimed" when the claim's chunk is
                // unloaded, the claim block is not the owner's primary, or the owner has been away
                // long enough for land protection to lapse.
                if (DroneWorld.Claim(_world, _ownerData, pos) != EnumLandClaimOwner.None) continue;
                if (SalvageGuards.InsideClaim(pos)) continue;

                // Never wrench a switch, button, relay or pressure plate. They are salvage-tagged
                // like any other metal fitting, but they are what opens a POI's doors, and the block
                // cannot be crafted or looted back - so one wrench swing breaks that POI forever.
                if (!settings.SalvageSwitches && SalvageGuards.IsTriggerBlock(b)) continue;

                // A trader area also reports as unclaimed, so the check above WAVES IT THROUGH. It
                // has to be rejected on its own, or the drone strips the trader's workstations.
                if (_world.IsWithinTraderArea(pos)) continue;

                // Never scrap a working vending machine by default: it is a shop the whole server
                // can still buy from, and one a player can rent and stock. Its broken shells are a
                // different block and stay salvageable.
                if (!settings.SalvageVendingMachines && SalvageGuards.IsWorkingVendingMachine(b)) continue;

                // Never scrap a workstation by default: it's a thing the player uses, and it may
                // still hold their materials.
                if (!settings.SalvageWorkstations && IsWorkstation(_world, pos)) continue;

                if (settings.ExcludedBlocks.Count > 0 && settings.ExcludedBlocks.Contains(b.GetBlockName())) continue;

                if (!settings.SalvageInPOIs && IsInsidePOI(_world, pos)) continue;

                // Never wrench a container that still holds loot the player hasn't taken. Many
                // world containers generate their loot only on first open, so an untouched one
                // can read empty yet still pay out - leave anything untouched, non-empty, or
                // player-owned for the player, and only salvage a container once it's emptied.
                if (HoldsUnlootedContents(_world, pos)) continue;

                if (!pacer.TrySpend(secondsPerStep)) break;

                // Yield this stage's salvage, then knock it down one downgrade step. The downgraded
                // stage is picked up on a later pass, so a car comes apart stage by stage, exactly
                // like wrenching it by hand.
                //
                // The drone counts as holding a wrench while the drops are worked out, so the
                // bonuses vanilla gates on holding one reach it too. Armed one call wide, released
                // in the finally - see SalvageToolContext.
                if (settings.CountAsHoldingSalvageTool) SalvageToolContext.Arm(settings.SalvageToolTags);
                try
                {
                    DroneWorld.EmitDrops(b, EnumDropEvent.Harvest, bv, _owner, _drone, rand);
                }
                finally
                {
                    SalvageToolContext.Disarm();
                }

                b.DamageBlock(_world, pos, bv, b.MaxDamage, _drone.entityId, null, _bUseHarvestTool: true);

                // Wrenching is loud. The drone's own noise is invisible to the AI director, so the
                // heat that breaking this stage by hand would have made is added explicitly - see
                // Heat. Without it the drone strips a POI without ever drawing a screamer.
                Heat.BlockBroken(_world, b, pos, settings.HeatMultiplier);
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
        /// True for forge, workbench, campfire, chemistry station, cement mixer - and any modded
        /// station. Detected off the tile entity rather than the block class on purpose: vanilla
        /// spreads workstations across three different block classes (Workstation = workbench and
        /// cement mixer, Forge = forge, Campfire = campfire AND chemistry station), so a class check
        /// is brittle and blind to modded ones, while they all share this one tile entity.
        /// </summary>
        private static bool IsWorkstation(World _world, Vector3i _pos)
        {
            return _world.GetTileEntity(_pos) is TileEntityWorkstation;
        }

        /// <summary>
        /// True when the position sits inside a POI's footprint. Only consulted when SalvageInPOIs
        /// is off, since wrenching cars and sinks as you clear a building is the module's main use.
        /// </summary>
        private static bool IsInsidePOI(World _world, Vector3i _pos)
        {
            DynamicPrefabDecorator decorator = _world.ChunkCache?.ChunkProvider?.GetDynamicPrefabDecorator();
            return decorator != null && decorator.GetPrefabAtPosition(_pos.ToVector3()) != null;
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
