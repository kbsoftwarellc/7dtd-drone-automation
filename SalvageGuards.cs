using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// The two guards that keep Auto-Salvage from wrecking things a player cannot rebuild: land
    /// claims, and the switches, buttons and wiring a POI needs in order to still work.
    ///
    /// Both are deliberately owner-independent and config-independent for the claim half. Auto-
    /// Salvage destroys blocks, so a guard that CAN be switched off, or that quietly stops applying,
    /// is worse than no guard - the failure is silent and permanent.
    /// </summary>
    public static class SalvageGuards
    {
        // Positions of every land claim block in the world that could cover the current work
        // bubble. Rebuilt once per module tick, then tested per block; a bubble is a few metres
        // across, so this is normally empty or one entry.
        private static readonly List<Vector3i> nearbyClaims = new List<Vector3i>();
        private static int claimHalfSize;

        /// <summary>
        /// Rebuilds the nearby-claim list for one module tick. Call once before walking the scan
        /// buffer, then use <see cref="InsideClaim"/> per block.
        ///
        /// This does NOT go through World.GetLandClaimOwner, and that is the point. That method has
        /// three ways to answer "unclaimed" for ground that has a claim block on it: the claim's
        /// chunk has to be loaded, the claim block has to be the owner's PRIMARY one, and the owner
        /// has to pass IsLandProtectionValidForPlayer - which goes false once they have been away
        /// longer than the server's land claim expiry. Any of those turns the drone loose inside a
        /// base whose owner is merely on holiday. Reading the persistent claim map directly has none
        /// of those holes: a claim block that exists protects its ground, full stop.
        /// </summary>
        public static void BeginTick(Vector3 _center, float _radius)
        {
            nearbyClaims.Clear();

            int size = GameStats.GetInt(EnumGameStats.LandClaimSize);
            if (size <= 0) size = 41;
            claimHalfSize = (size - 1) / 2;

            PersistentPlayerList players = GameManager.Instance?.GetPersistentPlayerList();
            if (players?.m_lpBlockMap == null) return;

            // A claim covers claimHalfSize blocks either side of its keystone, so any claim within
            // (half + bubble radius) of the scan centre can reach into the bubble.
            int cx = Utils.Fastfloor(_center.x);
            int cz = Utils.Fastfloor(_center.z);
            int span = claimHalfSize + Mathf.CeilToInt(_radius) + 1;

            foreach (KeyValuePair<Vector3i, PersistentPlayerData> kv in players.m_lpBlockMap)
            {
                Vector3i claim = kv.Key;
                if (Math.Abs(claim.x - cx) > span) continue;
                if (Math.Abs(claim.z - cz) > span) continue;
                nearbyClaims.Add(claim);
            }
        }

        /// <summary>
        /// True when the position is inside any land claim - the drone owner's own claim, an ally's,
        /// a stranger's. Vanilla claims are square in X/Z and unbounded in height, which is why only
        /// X and Z are compared here (World.GetLandClaimOwner does the same).
        /// </summary>
        public static bool InsideClaim(Vector3i _pos)
        {
            for (int i = 0; i < nearbyClaims.Count; i++)
            {
                Vector3i claim = nearbyClaims[i];
                if (Math.Abs(claim.x - _pos.x) <= claimHalfSize && Math.Abs(claim.z - _pos.z) <= claimHalfSize)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Block classes that make a POI work rather than furnish it: the switches and buttons that
        /// open its doors, the relays and pressure plates that wire them up, the quest generators a
        /// fetch quest turns on.
        ///
        /// These are salvage-tagged, so nothing above this guard stops the drone taking them, and
        /// the damage does not show up until a player walks the POI and finds the door that opens it
        /// is gone. A wrenched powerSwitch01 cannot be replaced - the block is not craftable and not
        /// in the loot pool, so the POI stays broken for the life of the save.
        ///
        /// Matched by type NAME up the inheritance chain rather than with `is`, on purpose. It costs
        /// nothing, it catches a modded block that derives from any of these, and it does not bind
        /// the DLL to types that may not exist in the oldest game build this mod claims to support.
        /// </summary>
        private static readonly HashSet<string> TriggerBlockTypes = new HashSet<string>
        {
            // powerSwitch01/02 - the POI door switch this guard exists for.
            "BlockActivateSwitch",
            // pushButtonSwitch01/02.
            "BlockActivate",
            // The pipe valve switches, and keyRackWood01.
            "BlockActivateSingle",
            // questGeneratorSmall/Large.
            "BlockQuestActivate",
            // Everything wired: electricwirerelay, electrictimerrelay, pressureplate(Long),
            // powered doors, powered lights, traps. BlockSwitch, BlockPressurePlate,
            // BlockTimerRelay, BlockPoweredDoor, BlockPoweredLight and BlockPoweredTrap all
            // derive from this one.
            "BlockPowered",
            // pipeSmallRedTest3m and anything else that downgrades on a trigger.
            "BlockTriggerDowngrade",
        };

        /// <summary>
        /// True when the block is a switch, button, relay, pressure plate or other trigger that a
        /// POI (or a player's wiring) depends on. See <see cref="TriggerBlockTypes"/>.
        /// </summary>
        public static bool IsTriggerBlock(Block _b)
        {
            for (Type t = _b?.GetType(); t != null; t = t.BaseType)
            {
                if (TriggerBlockTypes.Contains(t.Name)) return true;
                if (t.Name == "Block") break;
            }
            return false;
        }
    }
}
