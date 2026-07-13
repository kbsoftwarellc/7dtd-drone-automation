using System;
using System.IO;
using System.Reflection;
using System.Xml;
using HarmonyLib;

namespace DroneAutomation
{
    /// <summary>
    /// Drone Automation Mods — a pack of installable junk-drone modules, each opt-in and each
    /// unlocked by a looted schematic. Everything runs server-side via a single Harmony postfix
    /// on EntityDrone.OnUpdateEntity (see DronePatch), so clients install nothing.
    /// </summary>
    public class DroneAutomationMod : IModApi
    {
        // Must differ from every other mod's Harmony id (LootVacuum uses com.tehaon.lootvacuum).
        public const string HarmonyId = "com.tehaon.droneautomation";

        // Drone-mod item_modifier names. Matched against EntityDrone.OriginalItemValue.Modifications
        // by ItemClass.Name, exactly as the game's own drone-mod code does.
        public const string AutoLootModuleName    = "modRoboticDroneAutoLootMod";
        public const string AutoSalvageModuleName = "modRoboticDroneAutoSalvageMod";
        public const string AutoHarvestModuleName = "modRoboticDroneAutoHarvestMod";
        public const string AutoRepairModuleName  = "modRoboticDroneAutoRepairMod";
        public const string AutoPlantModuleName   = "modRoboticDroneAutoPlantMod";

        // Enhancement meta-modules: no core of their own, they scale every installed core's knobs.
        public const string OverclockModuleName   = "modRoboticDroneOverclockMod";
        public const string AntennaModuleName     = "modRoboticDroneAntennaMod";

        public static string ModPath;

        /// <summary>When set, every module logs (throttled) why it is or isn't acting.</summary>
        public static bool Debug;

        /// <summary>
        /// When set, a drone told to hold position (the vanilla "stay" command) keeps working the
        /// ground around the spot it was parked at, instead of around its owner. Its owner must still
        /// be online, but no longer has to be standing there - so a drone parked in the farm keeps
        /// reaping while you're out looting. Clear it to make a parked drone idle instead.
        /// </summary>
        public static bool WorkWhileParked = true;

        /// <summary>
        /// How far a FOLLOWING drone may be from its owner and still work (metres; 0 disables).
        /// The block modules act around the owner but deposit into the drone's bag, and drones are
        /// exempt from the chunk-loaded check, so without this a drone abandoned across the map keeps
        /// harvesting and salvaging around the player. A parked drone is exempt: working away from
        /// its owner is the entire point of parking it.
        /// </summary>
        public static float MaxOwnerDistance = 25f;

        /// <summary>Auto-Loot tunables. Generous by default - a mobile version of the loot vacuum.</summary>
        public static VacuumSettings AutoLootSettings = new VacuumSettings
        {
            ContainerRadius = 8f,
            EntityRadius = 15f,
            VerticalRadius = 6f,
        };

        /// <summary>Auto-Salvage tunables.</summary>
        public static SalvageSettings SalvageSettings = new SalvageSettings();

        /// <summary>Auto-Harvest tunables.</summary>
        public static HarvestSettings HarvestSettings = new HarvestSettings();

        /// <summary>Auto-Repair tunables.</summary>
        public static RepairSettings RepairSettings = new RepairSettings();

        /// <summary>Auto-Plant tunables.</summary>
        public static PlantSettings PlantSettings = new PlantSettings();

        /// <summary>Overclock meta-module tunables (speed boost for every core).</summary>
        public static OverclockSettings OverclockSettings = new OverclockSettings();

        /// <summary>Wide-Band Antenna meta-module tunables (reach boost for every core).</summary>
        public static AntennaSettings AntennaSettings = new AntennaSettings();

        public void InitMod(Mod _modInstance)
        {
            try
            {
                ModPath = _modInstance.Path;
                LoadSettings();

                Harmony harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());

                int patched = 0;
                foreach (var _ in harmony.GetPatchedMethods()) patched++;
                if (patched == 0) Log.Warning("[DroneAutomation] No methods patched — every module will do nothing.");

                Log.Out($"[DroneAutomation] InitMod complete — {patched} method(s) patched, server-side only. Path: {ModPath}");
            }
            catch (Exception e)
            {
                Log.Error("[DroneAutomation] InitMod failed: " + e);
            }
        }

        private static void LoadSettings()
        {
            string path = Path.Combine(ModPath, "droneautomation.xml");
            if (!File.Exists(path))
            {
                AutoLootSettings.Clamp();
                return;
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(path);

                ReadVacuum(doc.SelectSingleNode("/droneautomation/autoLoot"), AutoLootSettings);
                ReadSalvage(doc.SelectSingleNode("/droneautomation/autoSalvage"), SalvageSettings);
                ReadHarvest(doc.SelectSingleNode("/droneautomation/autoHarvest"), HarvestSettings);
                ReadRepair(doc.SelectSingleNode("/droneautomation/autoRepair"), RepairSettings);
                ReadPlant(doc.SelectSingleNode("/droneautomation/autoPlant"), PlantSettings);
                ReadOverclock(doc.SelectSingleNode("/droneautomation/overclock"), OverclockSettings);
                ReadAntenna(doc.SelectSingleNode("/droneautomation/antenna"), AntennaSettings);

                XmlNode root = doc.SelectSingleNode("/droneautomation");
                if (root?.Attributes != null)
                {
                    ReadBool(root, "Debug", ref Debug);
                    ReadBool(root, "WorkWhileParked", ref WorkWhileParked);
                    ReadFloat(root, "MaxOwnerDistance", ref MaxOwnerDistance);
                    if (MaxOwnerDistance < 0f) MaxOwnerDistance = 0f;
                }
            }
            catch (Exception e)
            {
                Log.Warning("[DroneAutomation] Could not read droneautomation.xml, using defaults: " + e.Message);
            }

            AutoLootSettings.Clamp();
            SalvageSettings.Clamp();
            HarvestSettings.Clamp();
            RepairSettings.Clamp();
            PlantSettings.Clamp();
            OverclockSettings.Clamp();
            AntennaSettings.Clamp();
        }

        private static void ReadRepair(XmlNode _node, RepairSettings _settings)
        {
            if (_node?.Attributes == null) return;

            ReadFloat(_node, "Radius", ref _settings.Radius);
            ReadFloat(_node, "VerticalRadius", ref _settings.VerticalRadius);
            ReadFloat(_node, "SecondsPerBlock", ref _settings.SecondsPerBlock);
            ReadFloat(_node, "MaxCatchupSeconds", ref _settings.MaxCatchupSeconds);
            ReadFloat(_node, "LowQualityReach", ref _settings.LowQualityReach);
            ReadFloat(_node, "LowQualityTimeMult", ref _settings.LowQualityTimeMult);
        }

        private static void ReadSalvage(XmlNode _node, SalvageSettings _settings)
        {
            if (_node?.Attributes == null) return;

            ReadFloat(_node, "Radius", ref _settings.Radius);
            ReadFloat(_node, "VerticalRadius", ref _settings.VerticalRadius);
            ReadFloat(_node, "SecondsPerStep", ref _settings.SecondsPerStep);
            ReadFloat(_node, "MaxCatchupSeconds", ref _settings.MaxCatchupSeconds);
            ReadFloat(_node, "LowQualityReach", ref _settings.LowQualityReach);
            ReadFloat(_node, "LowQualityTimeMult", ref _settings.LowQualityTimeMult);
            ReadBool(_node, "SalvageWorkstations", ref _settings.SalvageWorkstations);
            ReadBool(_node, "SalvageInPOIs", ref _settings.SalvageInPOIs);

            // <exclude block="..."/> children: blocks the drone must never wrench.
            _settings.ExcludedBlocks.Clear();
            foreach (XmlNode child in _node.ChildNodes)
            {
                if (child.Name != "exclude") continue;
                string block = child.Attributes?["block"]?.Value;
                if (!string.IsNullOrEmpty(block)) _settings.ExcludedBlocks.Add(block);
            }
        }

        private static void ReadHarvest(XmlNode _node, HarvestSettings _settings)
        {
            if (_node?.Attributes == null) return;

            ReadFloat(_node, "Radius", ref _settings.Radius);
            ReadFloat(_node, "VerticalRadius", ref _settings.VerticalRadius);
            ReadFloat(_node, "SecondsPerTarget", ref _settings.SecondsPerTarget);
            ReadFloat(_node, "MaxCatchupSeconds", ref _settings.MaxCatchupSeconds);
            ReadFloat(_node, "LowQualityReach", ref _settings.LowQualityReach);
            ReadFloat(_node, "LowQualityTimeMult", ref _settings.LowQualityTimeMult);
        }

        private static void ReadVacuum(XmlNode _node, VacuumSettings _settings)
        {
            if (_node?.Attributes == null) return;

            ReadFloat(_node, "Radius", ref _settings.ContainerRadius);
            ReadFloat(_node, "EntityRadius", ref _settings.EntityRadius);
            ReadFloat(_node, "VerticalRadius", ref _settings.VerticalRadius);
            ReadFloat(_node, "SpeedMultiplier", ref _settings.SpeedMultiplier);
            ReadFloat(_node, "MinSecondsPerTarget", ref _settings.MinSecondsPerTarget);
            ReadFloat(_node, "ItemPickupSeconds", ref _settings.ItemPickupSeconds);
            ReadFloat(_node, "MaxCatchupSeconds", ref _settings.MaxCatchupSeconds);
            ReadFloat(_node, "SkipIfPlayerWithin", ref _settings.SkipIfPlayerWithin);
            ReadFloat(_node, "LowQualityReach", ref _settings.LowQualityReach);
            ReadFloat(_node, "LowQualityTimeMult", ref _settings.LowQualityTimeMult);
        }

        private static void ReadPlant(XmlNode _node, PlantSettings _settings)
        {
            if (_node?.Attributes == null) return;

            ReadFloat(_node, "Radius", ref _settings.Radius);
            ReadFloat(_node, "VerticalRadius", ref _settings.VerticalRadius);
            ReadFloat(_node, "SecondsPerPlant", ref _settings.SecondsPerPlant);
            ReadFloat(_node, "MaxCatchupSeconds", ref _settings.MaxCatchupSeconds);
            ReadFloat(_node, "LowQualityReach", ref _settings.LowQualityReach);
            ReadFloat(_node, "LowQualityTimeMult", ref _settings.LowQualityTimeMult);
        }

        private static void ReadOverclock(XmlNode _node, OverclockSettings _settings)
        {
            if (_node?.Attributes == null) return;

            ReadFloat(_node, "Q1TimeMult", ref _settings.Q1TimeMult);
            ReadFloat(_node, "Q6TimeMult", ref _settings.Q6TimeMult);
        }

        private static void ReadAntenna(XmlNode _node, AntennaSettings _settings)
        {
            if (_node?.Attributes == null) return;

            ReadFloat(_node, "Q1ReachMult", ref _settings.Q1ReachMult);
            ReadFloat(_node, "Q6ReachMult", ref _settings.Q6ReachMult);
        }

        internal static void ReadFloat(XmlNode _node, string _attr, ref float _value)
        {
            string raw = _node.Attributes[_attr]?.Value;
            if (!string.IsNullOrEmpty(raw) && StringParsers.TryParseFloat(raw, out float parsed)) _value = parsed;
        }

        internal static void ReadBool(XmlNode _node, string _attr, ref bool _value)
        {
            string raw = _node.Attributes[_attr]?.Value;
            if (string.IsNullOrEmpty(raw)) return;
            _value = raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
