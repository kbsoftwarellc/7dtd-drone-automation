using System;
using System.Collections.Generic;

namespace DroneAutomation
{
    /// <summary>
    /// The kinds of thing Auto-Salvage can be told to take - the rows of the drone's
    /// "Salvage targets" talk menu.
    ///
    /// Kinds, not block names, because of scale: vanilla 3.x has 1,367 salvage-capable blocks and
    /// nobody is going to tick those one at a time. The grouping is not invented here. Every vanilla
    /// block carries FilterTags - the subcategories the creative menu filters by (SC_automotive,
    /// SC_plumbing, SC_lighting ...) - and overhaul and POI-mod blocks almost always inherit them
    /// through Extends, so a modded sink lands under Plumbing without anyone listing it.
    ///
    /// Seven groups, because the talk menu is a 10-row grid with no scrollbar: seven categories plus
    /// "take everything", "take nothing" and "back" fill it exactly. The vanilla split, counted from
    /// 3.x blocks.xml: Vehicles 334, Appliances 327, Furniture/decor/signs 266, Containers 201,
    /// Lights 182, Plumbing 176, Everything else 107. A block tagged into two groups (187 of them,
    /// e.g. an appliance that is also a light) is taken when EITHER is picked. A block with no
    /// recognised tag falls into Everything else, so nothing salvageable is out of the menu's reach.
    ///
    /// The state lives in one player CVar per group, 1 = take. 0 is "leave alone", which is also
    /// what an unwritten CVar reads as - so a player who has never opened the menu has picked
    /// nothing, and Auto-Salvage starts empty. That is deliberate (see SalvageVoice.Idle for how the
    /// drone says so rather than standing there looking broken), and it is the right way round for
    /// EntityBuffs.Write, which drops CVars at 0: only the choices a player made reach the save.
    /// </summary>
    public static class SalvageCategories
    {
        public sealed class Category
        {
            public readonly int Bit;
            public readonly string Name;
            public readonly string CVar;
            public readonly string[] Tags;

            public Category(int _index, string _name, string _cvar, params string[] _tags)
            {
                Bit = 1 << _index;
                Name = _name;
                CVar = _cvar;
                Tags = _tags;
            }
        }

        /// <summary>
        /// Menu order. The CVar names are what Config/buffs.xml and dialogs.xml write and test; the
        /// names are the english menu labels (daDialogSalv*), so what chat says matches the menu.
        /// </summary>
        public static readonly Category[] All =
        {
            new Category(0, "Vehicles",                   "daSalvVehicles",   "SC_automotive"),
            new Category(1, "Appliances and electrics",   "daSalvAppliances", "SC_electrical", "SC_helperElectrical"),
            new Category(2, "Lights",                     "daSalvLights",     "SC_lighting"),
            new Category(3, "Sinks, toilets and pipes",   "daSalvPlumbing",   "SC_plumbing"),
            new Category(4, "Furniture, decor and signs", "daSalvFurniture",  "SC_residential", "SC_helperResidential",
                                                                              "SC_commercial", "SC_helperCommercial",
                                                                              "SC_decor", "SC_signs"),
            new Category(5, "Emptied containers",         "daSalvContainers", "SC_loot", "SC_treasureRoom"),
            new Category(6, "Everything else",            "daSalvOther",      "SC_industrial", "SC_helperIndustrial",
                                                                              "SC_doors", "SC_traps", "SC_construction",
                                                                              "SC_helperOutdoor", "SC_playerHelpers",
                                                                              "SC_questblocks"),
        };

        private const int OtherBit = 1 << 6;

        /// <summary>
        /// Set once by the server when StartTargets="all" has turned every group on for a player, so
        /// it happens once and a player who later picks nothing keeps nothing. See SeedIfNew.
        /// </summary>
        public const string SeededCVar = "daSalvSeeded";

        // Block id -> group bits. Blocks never change after load, so each is worked out once.
        private static readonly Dictionary<int, int> maskById = new Dictionary<int, int>();
        private static readonly object gate = new object();

        /// <summary>Which groups a block belongs to. Never 0: an untagged block is Other.</summary>
        public static int Of(Block _b)
        {
            if (_b == null) return OtherBit;

            lock (gate)
            {
                if (maskById.TryGetValue(_b.blockID, out int cached)) return cached;
            }

            int mask = 0;
            string[] tags = _b.FilterTags;
            if (tags != null)
            {
                for (int t = 0; t < tags.Length; t++)
                {
                    string tag = tags[t]?.Trim();
                    if (string.IsNullOrEmpty(tag)) continue;

                    for (int c = 0; c < All.Length; c++)
                    {
                        string[] want = All[c].Tags;
                        for (int w = 0; w < want.Length; w++)
                        {
                            if (string.Equals(tag, want[w], StringComparison.OrdinalIgnoreCase)) mask |= All[c].Bit;
                        }
                    }
                }
            }
            if (mask == 0) mask = OtherBit;

            lock (gate) maskById[_b.blockID] = mask;
            return mask;
        }

        /// <summary>The groups this player has picked in the talk menu.</summary>
        public static int Picked(EntityPlayer _player)
        {
            if (_player?.Buffs == null) return 0;

            int mask = 0;
            for (int i = 0; i < All.Length; i++)
            {
                if (_player.Buffs.GetCustomVar(All[i].CVar) != 0f) mask |= All[i].Bit;
            }
            return mask;
        }

        /// <summary>
        /// "Vehicles; Lights" - or the given word when the mask is empty. Semicolons, because the
        /// labels themselves have commas in them.
        /// </summary>
        public static string Names(int _mask, string _none)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < All.Length; i++)
            {
                if ((_mask & All[i].Bit) != 0) names.Add(All[i].Name);
            }
            return names.Count == 0 ? _none : string.Join("; ", names.ToArray());
        }

        /// <summary>
        /// StartTargets="all": the first time the server sees a player with the module, turn every
        /// group on for them - the pre-0.12 behaviour, for a server that wants it. Once only (the
        /// SeededCVar latch), and never over a player who has already picked something themselves.
        ///
        /// EntityAlive.SetCVar rather than Buffs.SetCustomVar for the reason DronePatch.PublishOne
        /// gives: SetCustomVar gained a parameter in 3.1, SetCVar is the same on every build. It
        /// net-syncs the changed values, so the menu rows read ON when the player next opens it.
        /// </summary>
        public static void SeedIfNew(EntityPlayer _player, bool _startWithAll)
        {
            if (!_startWithAll || _player?.Buffs == null) return;
            if (_player.Buffs.GetCustomVar(SeededCVar) != 0f) return;

            if (Picked(_player) == 0)
            {
                for (int i = 0; i < All.Length; i++) _player.SetCVar(All[i].CVar, 1f);
            }
            _player.SetCVar(SeededCVar, 1f);
        }
    }
}
