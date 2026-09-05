using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace DroneAutomation
{
    /// <summary>
    /// What Auto-Salvage is and is not allowed to wrench, and where that list lives.
    ///
    /// The problem this solves is one of scale. Vanilla 3.2 ships 6,633 blocks, and 1,447 of them
    /// are salvage-capable once you resolve Extends - nobody is going to name them one at a time,
    /// and an overhaul changes the list anyway. So rules are GLOB patterns over block names
    /// ("cntVendingMachine*"), not a fixed enumeration, and the names themselves are learned in
    /// game rather than looked up: the drone reports what it just took, and you exclude it from
    /// that report. See SalvageLedger.
    ///
    /// Allow beats deny on purpose. It is the only order that lets "none of this family except that
    /// one" be said at all: deny "cnt*", allow "cntToilet*". The reverse order makes the second rule
    /// unsayable.
    ///
    /// Rules added in game are written to the SAVE, not to the mod folder: they are decisions about
    /// one world, they must survive a mod update, and a mod folder is often read-only on a hosted
    /// server. The XML file's rules are merged in on top at load, so a server op can still ship a
    /// baseline in droneautomation.xml.
    /// </summary>
    public static class SalvageRules
    {
        private const string FileName = "droneautomation-salvage.xml";

        // Patterns, lower-cased, in insertion order. Small enough (tens of entries, checked once per
        // salvage action) that a linear walk is the right data structure.
        private static readonly List<string> deny = new List<string>();
        private static readonly List<string> allow = new List<string>();

        // Block families already seen on this save, so a first encounter can be announced once and
        // never again. Kept here rather than in the ledger because it is the half that persists.
        private static readonly HashSet<string> seenFamilies = new HashSet<string>();

        private static readonly object gate = new object();

        // The save the loaded rules belong to. Compared on every use rather than latched, because a
        // single-player host can quit to the menu and load a DIFFERENT world without the process
        // restarting - and rules from the first world must not follow them into the second, nor be
        // written back over the first world's file.
        private static string loadedFor;

        /// <summary>Patterns from droneautomation.xml, re-applied whenever the save file is loaded.</summary>
        public static readonly List<string> ConfigDeny = new List<string>();

        public static IEnumerable<string> Deny { get { lock (gate) return new List<string>(deny); } }
        public static IEnumerable<string> Allow { get { lock (gate) return new List<string>(allow); } }

        /// <summary>
        /// The decision for one block. <paramref name="_unseenPolicyAllows"/> is what to do with a
        /// family this save has never salvaged before - see SalvageSettings.NewTargetPolicy.
        /// </summary>
        public static bool MaySalvage(string _blockName, bool _unseenPolicyAllows, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(_blockName)) return false;

            EnsureLoaded();
            string name = _blockName.ToLowerInvariant();

            lock (gate)
            {
                for (int i = 0; i < allow.Count; i++)
                {
                    if (Glob(name, allow[i])) { reason = "allowed by " + allow[i]; return true; }
                }
                for (int i = 0; i < deny.Count; i++)
                {
                    if (Glob(name, deny[i])) { reason = "excluded by " + deny[i]; return false; }
                }

                if (!_unseenPolicyAllows && !seenFamilies.Contains(Family(_blockName).ToLowerInvariant()))
                {
                    reason = "new target, awaiting /das allow";
                    return false;
                }
            }
            return true;
        }

        /// <summary>True the first time this save meets a family; records it as seen.</summary>
        public static bool MarkSeen(string _blockName)
        {
            EnsureLoaded();
            string fam = Family(_blockName).ToLowerInvariant();
            lock (gate)
            {
                if (!seenFamilies.Add(fam)) return false;
            }
            Save();
            return true;
        }

        public static bool AddDeny(string _pattern) => Add(deny, allow, _pattern);
        public static bool AddAllow(string _pattern) => Add(allow, deny, _pattern);

        /// <summary>Drops a pattern from both lists. Returns how many rules went.</summary>
        public static int Forget(string _pattern)
        {
            if (string.IsNullOrEmpty(_pattern)) return 0;
            EnsureLoaded();
            string p = _pattern.ToLowerInvariant();
            int n;
            lock (gate)
            {
                n = deny.RemoveAll(x => x == p) + allow.RemoveAll(x => x == p);
            }
            if (n > 0) Save();
            return n;
        }

        private static bool Add(List<string> _to, List<string> _from, string _pattern)
        {
            if (string.IsNullOrEmpty(_pattern)) return false;
            EnsureLoaded();
            string p = _pattern.ToLowerInvariant();
            lock (gate)
            {
                _from.Remove(p);              // a rule can only mean one thing at a time
                if (_to.Contains(p)) return false;
                _to.Add(p);
            }
            Save();
            return true;
        }

        /// <summary>
        /// The name a player would recognise as "this kind of thing". Vanilla spells a variant with
        /// trailing digits and a handful of stock suffixes - cntCar03SedanDamage0v01, Damage1v02 and
        /// so on are one sedan to everybody but the block list - so those are trimmed, and what is
        /// left is what a wildcard should be offered around.
        /// </summary>
        public static string Family(string _blockName)
        {
            if (string.IsNullOrEmpty(_blockName)) return "";

            string s = _blockName;
            for (bool cut = true; cut && s.Length > 1; )
            {
                cut = false;

                foreach (string suffix in VariantSuffixes)
                {
                    if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        s = s.Substring(0, s.Length - suffix.Length);
                        cut = true;
                    }
                }

                int end = s.Length;
                while (end > 1 && char.IsDigit(s[end - 1])) end--;
                if (end != s.Length) { s = s.Substring(0, end); cut = true; }

                // "v01" leaves its v behind once the digits are gone.
                if (s.Length > 2 && (s[s.Length - 1] == 'v' || s[s.Length - 1] == 'V'))
                {
                    s = s.Substring(0, s.Length - 1);
                    cut = true;
                }
            }
            return s;
        }

        private static readonly string[] VariantSuffixes =
        {
            "Master", "Helper", "RandomLootHelper", "Damage", "Destroyed", "Broken", "Open", "Closed",
            "Empty", "Full", "Insecure", "_Player",
        };

        /// <summary>
        /// Case-insensitive glob over '*' only. Deliberately not a regex: these patterns are typed
        /// into chat by players, and "cnt*" surprising nobody is worth more than lookahead.
        /// </summary>
        public static bool Glob(string _name, string _pattern)
        {
            if (string.IsNullOrEmpty(_pattern)) return false;
            if (_pattern == "*") return true;
            if (_pattern.IndexOf('*') < 0) return string.Equals(_name, _pattern, StringComparison.OrdinalIgnoreCase);

            string[] parts = _pattern.Split('*');
            int at = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;

                int found = _name.IndexOf(part, at, StringComparison.OrdinalIgnoreCase);
                if (found < 0) return false;
                if (i == 0 && found != 0) return false;                    // anchored start
                at = found + part.Length;
            }
            // anchored end unless the pattern finished with '*'
            return parts[parts.Length - 1].Length == 0 || at == _name.Length;
        }

        private static void EnsureLoaded()
        {
            string path = Path();
            lock (gate)
            {
                if (loadedFor != null && loadedFor == path) return;
            }
            Load();
        }

        /// <summary>
        /// Reloads from the save. Called on first use rather than at InitMod, because
        /// GameIO.GetSaveGameDir() is meaningless until a world is actually loaded.
        /// </summary>
        public static void Load()
        {
            string path = Path();
            lock (gate)
            {
                loadedFor = path;
                deny.Clear();
                allow.Clear();
                seenFamilies.Clear();
                for (int i = 0; i < ConfigDeny.Count; i++)
                {
                    string p = ConfigDeny[i].ToLowerInvariant();
                    if (!deny.Contains(p)) deny.Add(p);
                }
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                lock (gate)
                {
                    foreach (XmlNode n in doc.SelectNodes("/droneautomationSalvage/deny"))
                    {
                        string p = n.Attributes?["block"]?.Value?.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(p) && !deny.Contains(p)) deny.Add(p);
                    }
                    foreach (XmlNode n in doc.SelectNodes("/droneautomationSalvage/allow"))
                    {
                        string p = n.Attributes?["block"]?.Value?.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(p) && !allow.Contains(p)) allow.Add(p);
                    }
                    foreach (XmlNode n in doc.SelectNodes("/droneautomationSalvage/seen"))
                    {
                        string f = n.Attributes?["family"]?.Value?.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(f)) seenFamilies.Add(f);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[DroneAutomation] Could not read " + FileName + ", starting from the config's rules: " + e.Message);
            }
        }

        /// <summary>
        /// Writes the whole file. It is a few kilobytes at worst and only written when a rule
        /// changes or a family is met for the first time, so there is nothing to be clever about.
        /// </summary>
        public static void Save()
        {
            string path = Path();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sb.AppendLine("<!-- Written by Drone Automation. Rules added in game with /das live here so they");
                sb.AppendLine("     survive a mod update; droneautomation.xml's own <exclude> entries are merged in");
                sb.AppendLine("     on top at load. Safe to edit by hand while the server is down. -->");
                sb.AppendLine("<droneautomationSalvage>");
                lock (gate)
                {
                    foreach (string p in deny) sb.AppendLine("\t<deny block=\"" + Escape(p) + "\"/>");
                    foreach (string p in allow) sb.AppendLine("\t<allow block=\"" + Escape(p) + "\"/>");
                    foreach (string f in seenFamilies) sb.AppendLine("\t<seen family=\"" + Escape(f) + "\"/>");
                }
                sb.AppendLine("</droneautomationSalvage>");

                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception e)
            {
                Log.Warning("[DroneAutomation] Could not write " + FileName + ": " + e.Message);
            }
        }

        private static string Escape(string _s)
        {
            return _s?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        /// <summary>
        /// Where this world's rules live. Recomputed every time: GameIO.GetSaveGameDir() is
        /// meaningless before a world loads and different after a second one does, so caching it
        /// is how rules end up in the wrong save.
        /// </summary>
        private static string Path()
        {
            try
            {
                string dir = GameIO.GetSaveGameDir();
                return string.IsNullOrEmpty(dir) ? null : System.IO.Path.Combine(dir, FileName);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
