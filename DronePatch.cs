using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// Single entry point for every drone module. Runs server-side off EntityDrone.OnUpdateEntity;
    /// the drone already carries an OwnerID, a Bag and an EntityLockContext, and World.cs exempts
    /// drones from the chunk-loaded check, so it ticks wherever it follows you - or wherever you
    /// parked it.
    ///
    /// Each installed module gets its own paced core, held per-drone in a weak table so despawned
    /// drones do not leak. Owner resolution, the bag lock check and the choice of work anchor
    /// (parked spot vs owner) are shared by all modules.
    /// </summary>
    [HarmonyPatch(typeof(EntityDrone), nameof(EntityDrone.OnUpdateEntity))]
    public static class DroneModulePatch
    {
        private static readonly ConditionalWeakTable<EntityDrone, VacuumCore> autoLootCores =
            new ConditionalWeakTable<EntityDrone, VacuumCore>();
        private static readonly ConditionalWeakTable<EntityDrone, SalvageCore> salvageCores =
            new ConditionalWeakTable<EntityDrone, SalvageCore>();
        private static readonly ConditionalWeakTable<EntityDrone, HarvestCore> harvestCores =
            new ConditionalWeakTable<EntityDrone, HarvestCore>();
        private static readonly ConditionalWeakTable<EntityDrone, RepairCore> repairCores =
            new ConditionalWeakTable<EntityDrone, RepairCore>();
        private static readonly ConditionalWeakTable<EntityDrone, PlantCore> plantCores =
            new ConditionalWeakTable<EntityDrone, PlantCore>();
        private static readonly ConditionalWeakTable<EntityDrone, DefenseCore> defenseCores =
            new ConditionalWeakTable<EntityDrone, DefenseCore>();

        private static ulong lastDebugTick;
        private static ulong lastErrorTick;

        /// <summary>
        /// Nothing a module does is worth taking the server down for. This runs off
        /// EntityDrone.OnUpdateEntity, so an escaping exception would throw once per drone per tick,
        /// forever - one malformed block from any other mod would be enough. Swallow it, log it
        /// (throttled, or the log becomes the outage), and let the drone try again next tick.
        /// </summary>
        public static void Postfix(EntityDrone __instance)
        {
            try
            {
                Run(__instance);
            }
            catch (System.Exception e)
            {
                ulong now = GameTimer.Instance.ticks;
                if (now < lastErrorTick || now - lastErrorTick >= 200UL)
                {
                    lastErrorTick = now;
                    Log.Error($"[DroneAutomation][drone {__instance?.entityId}] module tick failed: {e}");
                }
            }
        }

        private static void Run(EntityDrone __instance)
        {
            // World.OnUpdateEntity runs this loop on clients too, for remote entities.
            ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsServer) return;

            if (__instance == null || __instance.IsDead()) return;
            if (__instance.bag == null) { Debug(__instance, "no bag"); return; }

            ItemValue droneItem = __instance.OriginalItemValue;
            ItemValue autoLoot    = GetModule(droneItem, DroneAutomationMod.AutoLootModuleName);
            ItemValue autoSalvage = GetModule(droneItem, DroneAutomationMod.AutoSalvageModuleName);
            ItemValue autoHarvest = GetModule(droneItem, DroneAutomationMod.AutoHarvestModuleName);
            ItemValue autoRepair  = GetModule(droneItem, DroneAutomationMod.AutoRepairModuleName);
            ItemValue autoPlant   = GetModule(droneItem, DroneAutomationMod.AutoPlantModuleName);
            ItemValue autoDefense = GetModule(droneItem, DroneAutomationMod.AutoDefenseModuleName);

            // Enhancement meta-modules have no core; they only scale the others, so they never
            // trip the early-out below on their own.
            ItemValue overclock = GetModule(droneItem, DroneAutomationMod.OverclockModuleName);
            ItemValue antenna   = GetModule(droneItem, DroneAutomationMod.AntennaModuleName);

            if (autoLoot == null && autoSalvage == null && autoHarvest == null && autoRepair == null && autoPlant == null && autoDefense == null)
            {
                Debug(__instance, "no automation module; mods=[" + DescribeMods(droneItem) + "]");
                return;
            }

            World world = GameManager.Instance?.World;
            if (world == null) return;

            EntityPlayer owner = VacuumCore.ResolveOwner(world, __instance.OwnerID, out PersistentPlayerData ownerData);
            if (owner == null) { Debug(__instance, "owner not resolved from OwnerID=" + (__instance.OwnerID?.ReadablePlatformUserIdentifier ?? "null")); return; }

            // Keep the talk menu's switch buffs on the owner. This has to run before any of the
            // early returns below, or a player who has switched everything off could never switch
            // anything back on. See PrimeToggleBuffs for why the menu needs them pre-seated.
            PrimeToggleBuffs(owner);

            // A module the owner has switched off in the drone's talk menu is treated as absent for
            // this pass: the mod stays in its slot, keeps its quality, and costs nothing to put back.
            // Deliberately applied AFTER the hardware check above, so a drone whose every module is
            // switched off still reports that it HAS modules - "switched off" and "none installed"
            // are different problems and the debug log has to be able to tell them apart.
            if (IsSwitchedOff(owner, DroneAutomationMod.LootOffCVar))    autoLoot    = null;
            if (IsSwitchedOff(owner, DroneAutomationMod.SalvageOffCVar)) autoSalvage = null;
            if (IsSwitchedOff(owner, DroneAutomationMod.HarvestOffCVar)) autoHarvest = null;
            if (IsSwitchedOff(owner, DroneAutomationMod.RepairOffCVar))  autoRepair  = null;
            if (IsSwitchedOff(owner, DroneAutomationMod.PlantOffCVar))   autoPlant   = null;
            if (IsSwitchedOff(owner, DroneAutomationMod.DefenseOffCVar)) autoDefense = null;

            if (autoLoot == null && autoSalvage == null && autoHarvest == null && autoRepair == null && autoPlant == null && autoDefense == null)
            {
                Debug(__instance, "every installed module is switched off in the drone's talk menu");
                return;
            }

            DroneBoost boost = BuildBoost(overclock, antenna);

            bool didSomething = false;
            string harvestDetail = null;

            // Auto-Defense fires the drone's own machine gun at nearby hostiles. It works around the
            // drone itself and never touches the bag, so it runs BEFORE the bag-lock check below - the
            // drone keeps laying down covering fire while you have its storage open - and independently
            // of the parked / near-owner gates that bound the block modules: a parked drone stands
            // sentry, a following one guards you.
            if (autoDefense != null)
            {
                DefenseCore core = defenseCores.GetValue(__instance, _ => new DefenseCore(DroneAutomationMod.DefenseSettings));
                didSomething |= core.Tick(__instance, autoDefense.Quality, boost);
            }

            // The drone's bag is client-authoritative over NetPackageBag, exactly like a loot bag.
            // Every module below reads or writes it, so pause them all while its owner has it open.
            if (LockManager.Instance.IsLockedServer(__instance))
            {
                Debug(__instance, didSomething ? "acted this pass" : "bag locked (owner has it open)");
                return;
            }

            // Where the block modules do their work. A drone told to hold position (the vanilla
            // "stay" command) works the ground it was parked on; otherwise it works around its owner.
            //
            // This is what makes the base modules worth having: Auto-Harvest, Auto-Plant and
            // Auto-Repair tend a base, but an owner-anchored bubble only fires while you're standing
            // in it - exactly when you don't need the help. Park the drone in the farm and it keeps
            // reaping while you're out looting.
            bool parked = __instance.OrderState == EntityDrone.Orders.Stay;
            if (parked && !DroneAutomationMod.WorkWhileParked)
            {
                Debug(__instance, "parked, and WorkWhileParked is off");
                return;
            }

            Vector3 scanCenter = parked ? __instance.SentryPos : owner.position;

            // A FOLLOWING drone must actually be with its owner before it works BLOCKS. Those modules
            // act around the owner but deposit into the drone's bag, and drones are exempt from the
            // chunk-loaded check, so an abandoned drone would otherwise keep harvesting and salvaging
            // around the player from across the map. A parked drone is exempt - working away from its
            // owner is why you parked it. Auto-Loot is exempt too: it works around itself, so it has
            // nothing to exploit.
            bool mayWorkBlocks = parked || IsNearOwner(__instance, owner);

            if (autoLoot != null)
            {
                VacuumCore core = autoLootCores.GetValue(__instance, _ => new VacuumCore(DroneAutomationMod.AutoLootSettings));
                didSomething |= core.Tick(world, owner, ownerData, __instance.OwnerID, new BagSink(__instance.bag), __instance.position, autoLoot.Quality, boost);
            }

            if (!mayWorkBlocks)
            {
                Debug(__instance, "following, but too far from owner to work blocks");
            }
            else
            {
                if (autoSalvage != null)
                {
                    SalvageCore core = salvageCores.GetValue(__instance, _ => new SalvageCore(DroneAutomationMod.SalvageSettings));
                    didSomething |= core.Tick(world, owner, ownerData, __instance, scanCenter, autoSalvage.Quality, boost);
                }

                if (autoHarvest != null)
                {
                    HarvestCore core = harvestCores.GetValue(__instance, _ => new HarvestCore(DroneAutomationMod.HarvestSettings));
                    didSomething |= core.Tick(world, owner, ownerData, __instance, scanCenter, autoHarvest.Quality, boost);
                    harvestDetail = core.LastScan;
                }

                if (autoRepair != null)
                {
                    RepairCore core = repairCores.GetValue(__instance, _ => new RepairCore(DroneAutomationMod.RepairSettings));
                    didSomething |= core.Tick(world, owner, ownerData, __instance, scanCenter, autoRepair.Quality, boost);
                }

                if (autoPlant != null)
                {
                    PlantCore core = plantCores.GetValue(__instance, _ => new PlantCore(DroneAutomationMod.PlantSettings));
                    didSomething |= core.Tick(world, owner, ownerData, __instance, scanCenter, autoPlant.Quality, boost);
                }
            }

            Debug(__instance, didSomething
                ? "acted this pass"
                : "active, nothing in range/afforded yet" + (harvestDetail != null ? "  ||  harvest: " + harvestDetail : ""));
        }

        /// <summary>
        /// Combines the installed enhancement meta-modules into one multiplier bundle. Each is
        /// quality-scaled (a Q6 module boosts more than a Q1), and an absent module contributes the
        /// identity, so a drone with neither gets DroneBoost.None and behaves exactly as before.
        /// </summary>
        private static DroneBoost BuildBoost(ItemValue _overclock, ItemValue _antenna)
        {
            if (_overclock == null && _antenna == null) return DroneBoost.None;

            float speed = _overclock != null ? DroneAutomationMod.OverclockSettings.SpeedMult(_overclock.Quality) : 1f;
            float reach = _antenna != null ? DroneAutomationMod.AntennaSettings.ReachMult(_antenna.Quality) : 1f;
            return new DroneBoost(speed, reach);
        }

        /// <summary>
        /// True when the drone is close enough to its owner to work blocks around them, or when the
        /// check is disabled (MaxOwnerDistance = 0). A following drone is normally right beside you -
        /// vanilla teleports it back when it falls behind - so this only ever fires for a drone that
        /// has been left somewhere.
        /// </summary>
        private static bool IsNearOwner(EntityDrone _drone, EntityPlayer _owner)
        {
            float max = DroneAutomationMod.MaxOwnerDistance;
            if (max <= 0f) return true;
            return (_drone.position - _owner.position).sqrMagnitude <= max * max;
        }

        /// <summary>
        /// True when the drone's owner has switched this module off from the talk menu.
        ///
        /// The menu writes the flag as a player CVar, because that is the only server-side state a
        /// vanilla client can change: the dialog's AddBuff action net-syncs, the server re-applies the
        /// buff (NetPackageAddRemoveBuff.ProcessPackage), and the buff's ModifyCVar effect runs on
        /// both ends. So the client sees its menu row flip instantly and the server - here - reads the
        /// same value a tick later, with no custom packet and nothing to install client-side.
        /// </summary>
        private static bool IsSwitchedOff(EntityPlayer _owner, string _cvar)
        {
            return _owner.Buffs != null && _owner.Buffs.GetCustomVar(_cvar) != 0f;
        }

        /// <summary>
        /// Make sure every buff the talk menu can add is already sitting on the owner, so that a menu
        /// click takes AddBuff's STACKING branch instead of its first-add branch.
        ///
        /// This is the fix for "the row only flips on the second click". EntityBuffs.AddBuff, given a
        /// buff the entity does not have, appends a BuffValue and fires nothing; onSelfBuffStart is
        /// raised later, when the buff first ticks. But the dialog redraws its rows exactly once per
        /// click (XUiC_DialogResponseList.Update rebuilds only while IsDirty, and OnPressResponse is
        /// the only thing that sets it) and a row's requirement is evaluated only during that rebuild,
        /// in XUiC_DialogResponseEntry.CurrentResponse. So a CVar written on buff start arrives after
        /// the only redraw and every row lags a click behind. When the buff is already present AddBuff
        /// instead calls FireEvent(onSelfBuffStack) inline, before it returns, so the CVar is written
        /// before Dialog.SelectResponse switches the statement and the rebuild sees the new value.
        ///
        /// Re-adding a buff that is already here would ALSO stack it - which would fire the effect and
        /// flip the player's switch from under them - hence the HasBuff guard. And priming is only
        /// harmless because these buffs deliberately have no onSelfBuffStart effect; adding one back to
        /// Config/buffs.xml would turn this method into a switch-flipper.
        ///
        /// Server-side AddBuff net-syncs to the clients attached to the entity (EntityBuffs.AddBuffNetwork),
        /// so the player's own client ends up holding the same buffs and its click stacks locally too.
        /// A buff lost for any reason (death cleanup, an old save) is simply re-seated on the next tick.
        /// </summary>
        private static void PrimeToggleBuffs(EntityPlayer _owner)
        {
            EntityBuffs buffs = _owner.Buffs;
            if (buffs == null) return;

            string[] names = DroneAutomationMod.ToggleBuffs;
            for (int i = 0; i < names.Length; i++)
            {
                if (!buffs.HasBuff(names[i])) buffs.AddBuff(names[i]);
            }
        }

        private static ItemValue GetModule(ItemValue _droneItem, string _moduleName)
        {
            ItemValue[] mods = _droneItem?.Modifications;
            if (mods == null) return null;

            for (int i = 0; i < mods.Length; i++)
            {
                ItemValue mod = mods[i];
                if (mod == null || mod.IsEmpty() || mod.ItemClass == null) continue;
                if (mod.ItemClass.Name == _moduleName) return mod;
            }
            return null;
        }

        private static string DescribeMods(ItemValue _droneItem)
        {
            ItemValue[] mods = _droneItem?.Modifications;
            if (mods == null) return "null";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < mods.Length; i++)
            {
                ItemValue mod = mods[i];
                if (mod == null || mod.IsEmpty()) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(mod.ItemClass?.Name ?? "?");
            }
            return sb.ToString();
        }

        /// <summary>Throttled to once every ~2s across all drones, so it does not flood the log.</summary>
        private static void Debug(EntityDrone _drone, string _msg)
        {
            if (!DroneAutomationMod.Debug) return;

            ulong now = GameTimer.Instance.ticks;
            if (now >= lastDebugTick && now - lastDebugTick < 40UL) return;
            lastDebugTick = now;

            Log.Out($"[DroneAutomation][drone {_drone.entityId}] {_msg}");
        }
    }
}
