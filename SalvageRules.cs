using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace DroneAutomation
{
    /// <summary>
    /// What Auto-Salvage is and is not allowed to wrench, by NAME, and where those rules live.
    ///
    /// The problem names solve is one of scale. Vanilla 3.2 ships 6,633 blocks, and 1,447 of them
    /// are salvage-capable once you resolve Extends - nobody is going to name them one at a time,
    /// and an overhaul changes the list anyway. So rules are GLOB patterns over block names
    /// ("cntVendingMachine*"), not a fixed enumeration, and the names themselves are learned in
    /// game rather than looked up: the drone reports what it just took, and you exclude it from
    /// that report. See SalvageLedger. The coarse choice - which KINDS of thing to take at all - is
    /// the talk menu's, see SalvageCategories; a name rule is how you make an exception to it.
    ///
    /// Three layers decide a block, in this order (see Decide):
    ///   1. the server's &lt;exclude&gt; list in droneautomation.xml. Absolute: every player keeps
    ///      their own rules now, so one player's "allow" must not lift what the server op ruled out.
    ///   2. the player's own /das rules. Allow beats skip on purpose: it is the only order that lets
    ///      "none of this family except that one" be said at all - skip "cnt*", allow "cntToilet*".
    ///      The reverse order makes the second rule unsayable. Both beat the menu, so you can skip
    ///      one car while Vehicles is picked, or allow one family while its group is not.
    ///   3. the groups the player picked in the talk menu, then the new-target policy for a family
    ///      this player's drone has never taken.
    ///
    /// PER PLAYER since 0.12, keyed by the player's platform id (PrimaryId.CombinedString) because
    /// an entity id is reassigned every session. Until then there was one list per world, and since
    /// chat has no admin check any player's /das skip changed every drone on the server.
    ///
    /// Rules are written to the SAVE, not to the mod folder: they are decisions about one world, they
    /// must survive a mod update, and a mod folder is often read-only on a hosted server. A file from
    /// before 0.12 holds that one world-wide list at the top level. It is kept, and is what a player's
    /// own rules start as the first time they change, so nobody loses a skip by updating.
    /// </summary>
    public static class SalvageRules
    {
        /// <summary>Why a block was or was not taken. The callers speak differently about each.</summary>
        public enum Verdict { Take, ServerRule, Skipped, NotPicked, NewTarget }

        private const string FileName = "droneautomation-salvage.xml";

        private sealed class Rules
        {
            // Patterns, lower-cased, in insertion order. Small enough (tens of entries, checked once
            // per salvage action) that a linear walk is the right data structure.
            public readonly List<string> Deny = new List<string>();
            public readonly List<string> Allow = new List<string>();

            // Block families this player's drone has already met, so a first encounter is announced
            // once and never again.
            public readonly HashSet<string> Seen = new HashSet<string>();

            public void CopyFrom(Rules _other)
            {
                Deny.AddRange(_other.Deny);
                Allow.AddRange(_other.Allow);
                Seen.UnionWith(_other.Seen);
            }
        }

        private static readonly Dictionary<string, Rules> players = new Dictionary<string, Rules>(StringComparer.Ordinal);

        // The pre-0.12 world-wide list, read from the top level of an old file. Anyone with no rules
        // of their own is judged by it, and gets a copy of it the first time their rules change.
        private static readonly Rules inherited = new Rules();

        private static readonly object gate = new object();

        // The save the loaded rules belong to. Compared on every use rather than latched, because a
        // single-player host can quit to the menu and load a DIFFERENT world without the process
        // restarting - and rules from the first world must not follow them into the second, nor be
        // written back over the first world's file.
        private static string loadedFor;

        /// <summary>Patterns from droneautomation.xml's &lt;exclude&gt; entries. They bind every player.</summary>
        public static readonly List<string> ConfigDeny = new List<string>();

        /// <summary>The key a player's rules are stored under, or null when it cannot be told.</summary>
        public static string Key(PersistentPlayerData _player)
        {
            return _player?.PrimaryId?.CombinedString;
        }

        public static string Key(int _entityId)
        {
            return Key(GameManager.Instance?.GetPersistentPlayerList()?.GetPlayerDataFromEntityID(_entityId));
        }

        public static List<string> Deny(string _key)
        {
            EnsureLoaded();
            lock (gate) return new List<string>(Find(_key).Deny);
        }

        public static List<string> Allow(string _key)
        {
            EnsureLoaded();
            lock (gate) return new List<string>(Find(_key).Allow);
        }

        public static List<string> ServerDeny
        {
            get { lock (gate) return new List<string>(ConfigDeny); }
        }

        /// <summary>
        /// The decision for one block. <paramref name="_picked"/> is the player's talk-menu groups
        /// (SalvageCategories.Picked); <paramref name="_unseenPolicyAllows"/> is what to do with a
        /// family this player's drone has never salvaged - see SalvageSettings.NewTargetPolicy.
        /// </summary>
        public static Verdict Decide(string _key, Block _block, int _picked, bool _unseenPolicyAllows, out string reason)
        {
            reason = null;
            string blockName = _block?.GetBlockName();
            if (string.IsNullOrEmpty(blockName)) { reason = "no block"; return Verdict.NotPicked; }

            EnsureLoaded();
            int groups = SalvageCategories.Of(_block);

            lock (gate)
            {
                for (int i = 0; i < ConfigDeny.Count; i++)
                {
                    if (Glob(blockName, ConfigDeny[i])) { reason = "excluded by the server (" + ConfigDeny[i] + ")"; return Verdict.ServerRule; }
                }

                Rules rules = Find(_key);
                for (int i = 0; i < rules.Allow.Count; i++)
                {
                    if (Glob(blockName, rules.Allow[i])) { reason = "allowed by your rule " + rules.Allow[i]; return Verdict.Take; }
                }
                for (int i = 0; i < rules.Deny.Count; i++)
                {
                    if (Glob(blockName, rules.Deny[i])) { reason = "skipped by your rule " + rules.Deny[i]; return Verdict.Skipped; }
                }

                if ((groups & _picked) == 0)
                {
                    reason = SalvageCategories.Names(groups, "Other") + " not picked in the drone's Salvage targets menu";
                    return Verdict.NotPicked;
                }

                if (!_unseenPolicyAllows && !rules.Seen.Contains(Family(blockName).ToLowerInvariant()))
                {
                    reason = "new target, awaiting /das allow";
                    return Verdict.NewTarget;
                }
            }
            return Verdict.Take;
        }

        /// <summary>True the first time this player's drone meets a family; records it as seen.</summary>
        public static bool MarkSeen(string _key, string _blockName)
        {
            if (_key == null) return false;
            EnsureLoaded();
            string fam = Family(_blockName).ToLowerInvariant();
            lock (gate)
            {
                if (!Own(_key).Seen.Add(fam)) return false;
            }
            Save();
            return true;
        }

        public static bool AddDeny(string _key, string _pattern) => Add(_key, _pattern, true);
        public static bool AddAllow(string _key, string _pattern) => Add(_key, _pattern, false);

        /// <summary>Drops a pattern from both of this player's lists. Returns how many rules went.</summary>
        public static int Forget(string _key, string _pattern)
        {
            if (_key == null || string.IsNullOrEmpty(_pattern)) return 0;
            EnsureLoaded();
            string p = _pattern.ToLowerInvariant();
            int n;
            lock (gate)
            {
                Rules rules = Own(_key);
                n = rules.Deny.RemoveAll(x => x == p) + rules.Allow.RemoveAll(x => x == p);
            }
            if (n > 0) Save();
            return n;
        }

        private static bool Add(string _key, string _pattern, bool _deny)
        {
            if (_key == null || string.IsNullOrEmpty(_pattern)) return false;
            EnsureLoaded();
            string p = _pattern.ToLowerInvariant();
            lock (gate)
            {
                Rules rules = Own(_key);
                List<string> to = _deny ? rules.Deny : rules.Allow;
                List<string> from = _deny ? rules.Allow : rules.Deny;
                from.Remove(p);              // a rule can only mean one thing at a time
                if (to.Contains(p)) return false;
                to.Add(p);
            }
            Save();
            return true;
        }

        /// <summary>The rules to judge by: the player's own, else the inherited list. Call under gate.</summary>
        private static Rules Find(string _key)
        {
            return _key != null && players.TryGetValue(_key, out Rules r) ? r : inherited;
        }

        /// <summary>The player's own rules, made from the inherited list on first write. Call under gate.</summary>
        private static Rules Own(string _key)
        {
            if (players.TryGetValue(_key, out Rules r)) return r;
            r = new Rules();
            r.CopyFrom(inherited);
            players[_key] = r;
            return r;
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
                players.Clear();
                inherited.Deny.Clear();
                inherited.Allow.Clear();
                inherited.Seen.Clear();
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                lock (gate)
                {
                    XmlNode root = doc.SelectSingleNode("/droneautomationSalvage");
                    if (root == null) return;

                    ReadRules(root, inherited);
                    foreach (XmlNode n in root.SelectNodes("player"))
                    {
                        string id = n.Attributes?["id"]?.Value;
                        if (string.IsNullOrEmpty(id)) continue;
                        if (!players.TryGetValue(id, out Rules rules)) players[id] = rules = new Rules();
                        ReadRules(n, rules);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[DroneAutomation] Could not read " + FileName + ", starting with no in-game rules: " + e.Message);
            }
        }

        private static void ReadRules(XmlNode _parent, Rules _into)
        {
            foreach (XmlNode n in _parent.SelectNodes("deny"))
            {
                string p = n.Attributes?["block"]?.Value?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(p) && !_into.Deny.Contains(p)) _into.Deny.Add(p);
            }
            foreach (XmlNode n in _parent.SelectNodes("allow"))
            {
                string p = n.Attributes?["block"]?.Value?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(p) && !_into.Allow.Contains(p)) _into.Allow.Add(p);
            }
            foreach (XmlNode n in _parent.SelectNodes("seen"))
            {
                string f = n.Attributes?["family"]?.Value?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(f)) _into.Seen.Add(f);
            }
        }

        /// <summary>
        /// Writes the whole file. It is a few kilobytes per player at worst and only written when a
        /// rule changes or a family is met for the first time, so there is nothing to be clever about.
        /// </summary>
        public static void Save()
        {
            string path = Path();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sb.AppendLine("<!-- Written by Drone Automation: each player's Auto-Salvage rules from /das, kept with");
                sb.AppendLine("     the world so a mod update cannot lose them. droneautomation.xml's <exclude> entries");
                sb.AppendLine("     are not copied here; they apply to everyone on top. Rules at the top level are the");
                sb.AppendLine("     single world-wide list from before 0.12 - a player's own list starts as a copy of");
                sb.AppendLine("     them. Safe to edit by hand while the server is down. -->");
                sb.AppendLine("<droneautomationSalvage>");
                lock (gate)
                {
                    WriteRules(sb, inherited, "\t");
                    foreach (KeyValuePair<string, Rules> kv in players)
                    {
                        sb.AppendLine("\t<player id=\"" + Escape(kv.Key) + "\">");
                        WriteRules(sb, kv.Value, "\t\t");
                        sb.AppendLine("\t</player>");
                    }
                }
                sb.AppendLine("</droneautomationSalvage>");

                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception e)
            {
                Log.Warning("[DroneAutomation] Could not write " + FileName + ": " + e.Message);
            }
        }

        private static void WriteRules(StringBuilder _sb, Rules _rules, string _indent)
        {
            foreach (string p in _rules.Deny) _sb.AppendLine(_indent + "<deny block=\"" + Escape(p) + "\"/>");
            foreach (string p in _rules.Allow) _sb.AppendLine(_indent + "<allow block=\"" + Escape(p) + "\"/>");
            foreach (string f in _rules.Seen) _sb.AppendLine(_indent + "<seen family=\"" + Escape(f) + "\"/>");
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
