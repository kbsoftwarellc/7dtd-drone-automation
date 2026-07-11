using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// Single entry point for every drone module. Runs server-side off EntityDrone.OnUpdateEntity;
    /// the drone already carries an OwnerID, a Bag and an EntityLockContext, and World.cs exempts
    /// drones from the chunk-loaded check, so it ticks wherever it follows you.
    ///
    /// Each installed module gets its own paced core, held per-drone in a weak table so despawned
    /// drones do not leak. Owner resolution and the bag lock check are shared by all modules.
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

        private static ulong lastDebugTick;

        public static void Postfix(EntityDrone __instance)
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

            if (autoLoot == null && autoSalvage == null && autoHarvest == null && autoRepair == null)
            {
                Debug(__instance, "no automation module; mods=[" + DescribeMods(droneItem) + "]");
                return;
            }

            // The drone's bag is client-authoritative over NetPackageBag, exactly like a loot bag.
            // Every module reads or writes it, so pause them all while its owner has it open.
            if (LockManager.Instance.IsLockedServer(__instance)) { Debug(__instance, "bag locked (owner has it open)"); return; }

            World world = GameManager.Instance?.World;
            if (world == null) return;

            EntityPlayer owner = VacuumCore.ResolveOwner(world, __instance.OwnerID, out PersistentPlayerData ownerData);
            if (owner == null) { Debug(__instance, "owner not resolved from OwnerID=" + (__instance.OwnerID?.ReadablePlatformUserIdentifier ?? "null")); return; }

            bool didSomething = false;

            if (autoLoot != null)
            {
                VacuumCore core = autoLootCores.GetValue(__instance, _ => new VacuumCore(DroneAutomationMod.AutoLootSettings));
                didSomething |= core.Tick(world, owner, ownerData, __instance.OwnerID, new BagSink(__instance.bag), __instance.position, autoLoot.Quality);
            }

            if (autoSalvage != null)
            {
                SalvageCore core = salvageCores.GetValue(__instance, _ => new SalvageCore(DroneAutomationMod.SalvageSettings));
                didSomething |= core.Tick(world, owner, ownerData, __instance, autoSalvage.Quality);
            }

            if (autoHarvest != null)
            {
                HarvestCore core = harvestCores.GetValue(__instance, _ => new HarvestCore(DroneAutomationMod.HarvestSettings));
                didSomething |= core.Tick(world, owner, ownerData, __instance, autoHarvest.Quality);
            }

            if (autoRepair != null)
            {
                RepairCore core = repairCores.GetValue(__instance, _ => new RepairCore(DroneAutomationMod.RepairSettings));
                didSomething |= core.Tick(world, ownerData, __instance, autoRepair.Quality);
            }

            Debug(__instance, didSomething ? "acted this pass" : "active, nothing in range/afforded yet");
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
