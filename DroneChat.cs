using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// The /das chat command - how a player tells Auto-Salvage what to leave alone by name, and asks
    /// how much ammo Auto-Defense has left.
    ///
    /// The coarse choice - which kinds of thing to take at all - is the drone's talk menu (Salvage
    /// targets, see SalvageCategories). Chat is for the exceptions, for two reasons. A talk menu row
    /// can only flip a number, not take a name, and the thing a player wants to exclude is usually
    /// already gone - they notice the vending machine is missing, not that it is about to go. So
    /// the entry point is "what did you just take", and the exclude takes a line number off that list.
    ///
    /// Every rule is the SENDER's own (SalvageRules is per player), so nobody's command reaches
    /// anybody else's drone and the command needs no admin check.
    ///
    /// Registered through ModEvents.ChatMessage rather than a Harmony patch: the game offers the
    /// hook, and an unmodified client can type into it, which is the whole point of a server-side
    /// mod.
    /// </summary>
    public static class DroneChat
    {
        public static ModEvents.EModEventResult OnChat(ref ModEvents.SChatMessageData _data)
        {
            // Our own replies come back through here with sender -1; without this the handler
            // would answer itself.
            if (_data.SenderEntityId == -1) return ModEvents.EModEventResult.Continue;

            string msg = _data.Message;
            if (string.IsNullOrEmpty(msg)) return ModEvents.EModEventResult.Continue;

            msg = msg.Trim();
            if (msg.Length == 0 || msg[0] != '/') return ModEvents.EModEventResult.Continue;

            string prefix = DroneAutomationMod.SalvageSettings.ChatPrefix;
            string[] parts = msg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!string.Equals(parts[0], prefix, StringComparison.OrdinalIgnoreCase))
                return ModEvents.EModEventResult.Continue;

            int who = _data.SenderEntityId;
            try
            {
                Run(who, parts, prefix);
            }
            catch (Exception e)
            {
                // A command that throws must not take the chat system with it.
                Log.Warning("[DroneAutomation] /" + prefix + " failed: " + e);
                Msg.Tell(who, "Auto-Salvage: that went wrong, and the server log has why.");
            }

            // Swallow it, or every player sees the command text in global chat.
            return ModEvents.EModEventResult.StopHandlersAndVanilla;
        }

        private static void Run(int _who, string[] _parts, string _prefix)
        {
            string sub = _parts.Length > 1 ? _parts[1].ToLowerInvariant() : "help";
            string arg = _parts.Length > 2 ? _parts[2] : null;

            switch (sub)
            {
                case "last": Last(_who, arg, _prefix); break;
                case "skip":
                case "exclude": Skip(_who, arg, _parts.Length > 3 ? _parts[3] : null, _prefix); break;
                case "allow":
                case "include": Allow(_who, arg, _prefix); break;
                case "forget": Forget(_who, arg); break;
                case "undo": Undo(_who, _prefix); break;
                case "rules": Rules(_who, _prefix); break;
                case "list": List(_who, arg, _prefix); break;
                case "what": What(_who); break;
                case "ammo": Ammo(_who); break;
                default: Help(_who, _prefix); break;
            }
        }

        private static void Help(int _who, string p)
        {
            Msg.TellAll(_who, new List<string>
            {
                "Auto-Salvage - what it may and may not wrench:",
                $"  {p} last [n]        the last things it salvaged, newest first",
                $"  {p} skip <n|name>   never salvage that again (n is a line from 'last')",
                $"  {p} skip <n> exact  only that exact block, not the whole family",
                $"  {p} allow <name>    undo a skip, or permit a held-back target",
                $"  {p} rules           the rules in force",
                $"  {p} list <name*>    every salvageable block on this server matching that",
                $"  {p} forget <rule>   drop a rule",
                $"  {p} undo            drop your most recent skip",
                $"  {p} what            name the block you are looking at",
                "Names take * as a wildcard: cntVendingMachine* is every vending machine.",
                "Which kinds of thing it takes at all: talk to your drone > Automation modules > Auto-Salvage.",
                "These rules are yours alone - they change your drone, nobody else's.",
                "Auto-Defense:",
                $"  {p} ammo            rounds left in each of your drones",
            });
        }

        /// <summary>Exact rounds per drone. The talk menu can only say plenty / low / empty.</summary>
        private static void Ammo(int _who)
        {
            DefenseSettings settings = DroneAutomationMod.DefenseSettings;
            if (!DefenseCore.NeedsAmmo(settings))
            {
                Msg.Tell(_who, "Auto-Defense needs no ammo on this server.");
                return;
            }

            List<string> lines = DefenseCore.Report(_who, settings);
            if (lines.Count == 0)
            {
                Msg.Tell(_who, "Auto-Defense: none of your drones with the module is out right now.");
                return;
            }
            lines.Insert(0, "Auto-Defense ammo (Robotic Turret Ammo, loaded from the drone's bag):");
            Msg.TellAll(_who, lines);
        }

        internal static void Last(int _who, string _arg, string _prefix)
        {
            int n = 10;
            if (!string.IsNullOrEmpty(_arg) && int.TryParse(_arg, out int parsed)) n = Mathf.Clamp(parsed, 1, SalvageLedger.Size);

            List<SalvageLedger.Entry> recent = SalvageLedger.Recent(_who, n);
            if (recent.Count == 0)
            {
                Msg.Tell(_who, "Auto-Salvage has not taken anything yet - or the server has restarted since it did.");
                return;
            }

            List<string> lines = new List<string> { "Auto-Salvage, most recent first:" };
            for (int i = 0; i < recent.Count; i++)
            {
                SalvageLedger.Entry e = recent[i];
                lines.Add($"  {i + 1}. {e.BlockName}  at {e.Pos.x} {e.Pos.y} {e.Pos.z}");
            }
            lines.Add($"{_prefix} skip <number> to stop it taking that kind of thing.");
            Msg.TellAll(_who, lines);
        }

        internal static void Skip(int _who, string _arg, string _mode, string _prefix)
        {
            if (string.IsNullOrEmpty(_arg)) { Msg.Tell(_who, $"{_prefix} skip <number from '{_prefix} last', a block name, or a name*"); return; }

            string key = KeyOrSay(_who);
            if (key == null) return;

            bool exact = string.Equals(_mode, "exact", StringComparison.OrdinalIgnoreCase);
            string pattern = PatternFor(_who, _arg, exact, out string what);
            if (pattern == null) { Msg.Tell(_who, $"No line {_arg} in the log - try {_prefix} last."); return; }

            if (SalvageRules.AddDeny(key, pattern))
                Msg.Tell(_who, $"Auto-Salvage will leave {pattern} alone{what} - {SalvageCatalog.Describe(pattern)}.");
            else
                Msg.Tell(_who, $"Auto-Salvage already leaves {pattern} alone.");
        }

        private static void Allow(int _who, string _arg, string _prefix)
        {
            if (string.IsNullOrEmpty(_arg)) { Msg.Tell(_who, $"{_prefix} allow <block name or name*>"); return; }

            string key = KeyOrSay(_who);
            if (key == null) return;

            string pattern = PatternFor(_who, _arg, false, out _);
            if (pattern == null) { Msg.Tell(_who, $"No line {_arg} in the log - try {_prefix} last."); return; }

            // "allow" means "let it take these", which is either lifting a skip or granting a new
            // target. Lifting comes first: an exact match on an existing rule is nearly always
            // someone undoing themselves, and adding an allow that shadows their own deny would
            // leave two contradictory rules in the list.
            int dropped = SalvageRules.Forget(key, pattern);
            if (dropped > 0)
            {
                SalvageVoice.ForgetHeld(_who, pattern.TrimEnd('*'));
                Msg.Tell(_who, $"Dropped the rule for {pattern}. Auto-Salvage may take those again.");
                return;
            }

            if (SalvageRules.AddAllow(key, pattern))
            {
                SalvageVoice.ForgetHeld(_who, pattern.TrimEnd('*'));
                Msg.Tell(_who, $"Auto-Salvage may take {pattern} - {SalvageCatalog.Describe(pattern)}.");
            }
            else
            {
                Msg.Tell(_who, $"Auto-Salvage already takes {pattern}.");
            }
        }

        private static void Forget(int _who, string _arg)
        {
            if (string.IsNullOrEmpty(_arg)) { Msg.Tell(_who, "Name the rule to drop, exactly as 'rules' prints it."); return; }
            string key = KeyOrSay(_who);
            if (key == null) return;

            int n = SalvageRules.Forget(key, _arg);
            if (n > 0) { Msg.Tell(_who, $"Dropped {n} rule(s) for {_arg}."); return; }

            bool server = SalvageRules.ServerDeny.Exists(x => string.Equals(x, _arg, StringComparison.OrdinalIgnoreCase));
            Msg.Tell(_who, server ? $"{_arg} is the server's rule (droneautomation.xml), not yours to drop." : $"No rule for {_arg}.");
        }

        /// <summary>
        /// Drops this player's newest skip - for the one added by mistake, from chat or from the
        /// talk menu's "never take the last thing again" row. Rules keep the order they were added
        /// in, so the newest is the last.
        /// </summary>
        internal static void Undo(int _who, string _prefix)
        {
            string key = KeyOrSay(_who);
            if (key == null) return;

            string pattern = SalvageRules.NewestDeny(key);
            if (pattern == null) { Msg.Tell(_who, "You have no skips to undo."); return; }

            SalvageRules.Forget(key, pattern);
            SalvageVoice.ForgetHeld(_who, pattern.TrimEnd('*'));
            Msg.Tell(_who, $"Dropped your skip for {pattern} - Auto-Salvage takes those again if their group is picked. ({_prefix} rules shows the rest.)");
        }

        /// <summary>
        /// Everything that decides what this player's drone takes, in the order it is applied - so
        /// "why did it leave that?" has an answer on one screen.
        /// </summary>
        private static void Rules(int _who, string _prefix)
        {
            string key = SalvageRules.Key(_who);
            int picked = SalvageCategories.Picked(GameManager.Instance?.World?.GetEntity(_who) as EntityPlayer);
            int all = 0;
            foreach (SalvageCategories.Category c in SalvageCategories.All) all |= c.Bit;

            List<string> lines = new List<string>
            {
                "Auto-Salvage takes: " + SalvageCategories.Names(picked, "nothing yet"),
            };
            if (picked != all) lines.Add("  and leaves: " + SalvageCategories.Names(all & ~picked, "nothing"));
            lines.Add("  (change it: talk to your drone > Automation modules > Auto-Salvage)");

            List<string> allow = SalvageRules.Allow(key);
            List<string> deny = SalvageRules.Deny(key);
            if (allow.Count + deny.Count > 0)
            {
                lines.Add("Your rules, ahead of the menu (allow beats skip):");
                foreach (string p in allow) lines.Add("  allow  " + p);
                foreach (string p in deny) lines.Add("  skip   " + p);
            }
            else
            {
                lines.Add($"No rules of your own. {_prefix} skip <name> leaves one kind alone, {_prefix} allow <name> takes one.");
            }

            List<string> server = SalvageRules.ServerDeny;
            if (server.Count > 0)
            {
                lines.Add("The server's rules, ahead of everything:");
                foreach (string p in server) lines.Add("  skip   " + p);
            }
            Msg.TellAll(_who, lines);
        }

        /// <summary>
        /// Every salvageable block on this server matching a pattern - the in-game answer to "what
        /// is this thing called". Read from the running game, so it covers whatever overhaul or POI
        /// mod is installed, which no published list could.
        /// </summary>
        private static void List(int _who, string _arg, string _prefix)
        {
            if (string.IsNullOrEmpty(_arg)) { Msg.Tell(_who, $"{_prefix} list <name*> - try {_prefix} list *vending*"); return; }

            const int Cap = 12;
            List<string> hits = SalvageCatalog.Matching(_arg, Cap, out int total);
            if (total == 0)
            {
                Msg.Tell(_who, $"No salvageable block matches {_arg}. Wildcards go anywhere: *sink*, cnt*, *Damage0v01.");
                return;
            }

            List<string> lines = new List<string> { $"{total} salvageable block(s) match {_arg}:" };
            foreach (string n in hits) lines.Add("  " + n);
            if (total > hits.Count) lines.Add($"  ... and {total - hits.Count} more - narrow the pattern to see them.");
            Msg.TellAll(_who, lines);
        }

        /// <summary>
        /// Names the block the player is looking at, so a target can be excluded BEFORE it is taken.
        ///
        /// Walked by hand rather than with a physics raycast: the collider path is client-side, and
        /// the server only has the player's position and rotation. Sampling the ray a quarter-block
        /// at a time finds the first solid block along it, which is all this needs.
        /// </summary>
        private static void What(int _who)
        {
            World world = GameManager.Instance?.World;
            if (!(world?.GetEntity(_who) is EntityPlayer player))
            {
                Msg.Tell(_who, "Cannot tell where you are looking from here.");
                return;
            }

            Ray ray = player.GetLookRay();
            for (float d = 0.5f; d <= 12f; d += 0.25f)
            {
                Vector3 p = ray.origin + ray.direction * d;
                Vector3i pos = World.worldToBlockPos(p);
                BlockValue bv = world.GetBlock(pos);
                if (bv.isair) continue;

                Block b = bv.Block;
                if (b == null) continue;

                string name = b.GetBlockName();
                if (!SalvageCatalog.IsSalvageable(b))
                {
                    Msg.Tell(_who, $"{name}  -  not something Auto-Salvage can take.");
                    return;
                }

                string family = SalvageRules.Family(name);
                string groups = SalvageCategories.Names(SalvageCategories.Of(b), "Other");
                bool policyAllows = DroneAutomationMod.SalvageSettings.NewTargetPolicyAllows;
                SalvageRules.Verdict verdict = SalvageRules.Decide(SalvageRules.Key(_who), b,
                    SalvageCategories.Picked(player), policyAllows, out string why);

                Msg.Tell(_who, $"{name}  (family {family}*, {groups})  -  " +
                                (verdict == SalvageRules.Verdict.Take
                                    ? "Auto-Salvage may take this" + (why != null ? " - " + why : "")
                                    : "left alone: " + why));
                return;
            }

            Msg.Tell(_who, "Nothing solid within 12m of where you are looking.");
        }

        /// <summary>
        /// The sender's rule key, or null after telling them why there isn't one. Only fails before
        /// the server has their persistent record, which nobody typing in chat should ever hit.
        /// </summary>
        private static string KeyOrSay(int _who)
        {
            string key = SalvageRules.Key(_who);
            if (key == null) Msg.Tell(_who, "Auto-Salvage cannot tell who you are yet - try again in a moment.");
            return key;
        }

        /// <summary>
        /// Turns a command argument into a rule pattern. A number is a line from the log, and a log
        /// line means the FAMILY by default - someone annoyed about a wrecked sedan means every
        /// sedan, not the one variant that happened to be in front of them - with "exact" for the
        /// literal block. Anything else is taken as typed.
        /// </summary>
        private static string PatternFor(int _who, string _arg, bool _exact, out string _note)
        {
            _note = "";
            if (int.TryParse(_arg, out int index))
            {
                if (!SalvageLedger.ByIndex(_who, index, out SalvageLedger.Entry e)) return null;
                if (_exact) { _note = " (that exact block only)"; return e.BlockName; }
                _note = $" (from log line {index}: {e.BlockName})";
                return e.Family + "*";
            }
            return _arg;
        }
    }
}
