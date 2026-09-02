using System;
using System.Collections.Generic;

namespace DroneAutomation
{
    /// <summary>
    /// Every block on THIS server that Auto-Salvage could ever wrench, read from the game's own
    /// block list at first use.
    ///
    /// It exists so a rule can be checked before it bites. A wildcard is easy to type and easy to
    /// get wrong - "cntCar03Sedan*" also covers cntCar03Sedan3Wide, which may or may not be what
    /// someone meant - so every rule the player adds reports how many blocks it actually matches
    /// and names a few, and "/das list <pattern>" answers the same question on its own.
    ///
    /// Read from Block.list rather than shipped as a table on purpose: vanilla 3.2 has 1,447
    /// salvage-capable blocks, an overhaul has its own, and a POI mod adds more. A list generated
    /// from the running game is right on every install; a list published anywhere else is right on
    /// exactly one.
    /// </summary>
    public static class SalvageCatalog
    {
        private static List<string> names;
        private static readonly object gate = new object();

        /// <summary>
        /// Salvageable = has a Harvest-event drop tagged as salvage. That tag is what marks
        /// wrenchable objects (cars, sinks, safes, machines) apart from terrain, ore and plants,
        /// which use other drop events and tags.
        /// </summary>
        public static bool IsSalvageable(Block _b)
        {
            if (_b?.itemsToDrop == null) return false;
            if (!_b.itemsToDrop.TryGetValue(EnumDropEvent.Harvest, out List<Block.SItemDropProb> list) || list == null) return false;

            for (int i = 0; i < list.Count; i++)
            {
                string tag = list[i].tag;
                if (!string.IsNullOrEmpty(tag) && tag.Contains("salvage")) return true;
            }
            return false;
        }

        /// <summary>
        /// Names of every salvage-capable block, built once. Blocks are loaded before any drone
        /// ticks and never change afterwards, so one pass over Block.list is enough.
        /// </summary>
        public static List<string> Names()
        {
            lock (gate)
            {
                if (names != null) return names;

                List<string> found = new List<string>();
                try
                {
                    Block[] all = Block.list;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            Block b = all[i];
                            if (b == null || !IsSalvageable(b)) continue;

                            string n = b.GetBlockName();
                            if (!string.IsNullOrEmpty(n)) found.Add(n);
                        }
                    }
                    found.Sort(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception e)
                {
                    Log.Warning("[DroneAutomation] Could not read the block list: " + e.Message);
                }

                names = found;
                return names;
            }
        }

        /// <summary>Salvage-capable blocks matching a glob, capped so a reply fits in chat.</summary>
        public static List<string> Matching(string _pattern, int _limit, out int _total)
        {
            _total = 0;
            List<string> hits = new List<string>();
            if (string.IsNullOrEmpty(_pattern)) return hits;

            string pattern = _pattern.ToLowerInvariant();
            foreach (string n in Names())
            {
                if (!SalvageRules.Glob(n.ToLowerInvariant(), pattern)) continue;
                _total++;
                if (hits.Count < _limit) hits.Add(n);
            }
            return hits;
        }

        /// <summary>"7 blocks: a, b, c, ..." - what a rule is about to cover, in one line.</summary>
        public static string Describe(string _pattern)
        {
            List<string> hits = Matching(_pattern, 3, out int total);
            if (total == 0) return "no salvageable block matches that yet";

            string sample = string.Join(", ", hits.ToArray());
            return total <= hits.Count
                ? $"{total} block(s): {sample}"
                : $"{total} blocks: {sample}, ...";
        }
    }
}
