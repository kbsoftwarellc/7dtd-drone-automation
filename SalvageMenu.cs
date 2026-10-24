using System.Collections.Generic;

namespace DroneAutomation
{
    /// <summary>
    /// The one-shot rows on the drone's "Auto-Salvage targets > More" page: never take the last
    /// thing it took again, tell me what it took, undo my last skip.
    ///
    /// They exist because a talk-menu row cannot name a block. Its text is resolved when the client
    /// parses dialogs.xml, so no row can ever read "the sedan it just took" - but the server knows
    /// what it just took (SalvageLedger), and chat can say it. So each row only raises a request
    /// CVar (Config/buffs.xml), and this acts on it from the drone's tick, answers in chat, and
    /// lowers it again so the same row works next time. The work is exactly /das skip 1, /das last
    /// and /das undo; the menu is how a player reaches them without typing.
    ///
    /// Polled from the owner's drone tick, for the same reason the module switches are read there:
    /// a buff's CVar write is the only thing a vanilla client can send.
    /// </summary>
    public static class SalvageMenu
    {
        public const string SkipLastCVar = "daSalvCmdSkipLast";
        public const string ListLastCVar = "daSalvCmdListLast";
        public const string UndoSkipCVar = "daSalvCmdUndoSkip";

        // A request raised again within this many ticks (~2 s) of being handled is taken as the same
        // press, not a new one. On a dedicated server one AddBuff can reach the player's buffs more
        // than once (the start-and-stack echo the module switches already guard against with "set"),
        // and lowering the flag cannot stop a late echo raising it again. Nobody means to skip twice
        // in two seconds; a real second press after that works.
        private const ulong RepeatTicks = 40UL;

        private static readonly Dictionary<string, ulong> handledAt = new Dictionary<string, ulong>();

        public static void Poll(EntityPlayer _owner)
        {
            if (_owner?.Buffs == null) return;

            string prefix = DroneAutomationMod.SalvageSettings.ChatPrefix;
            int who = _owner.entityId;

            if (Take(_owner, SkipLastCVar))
            {
                if (SalvageLedger.ByIndex(who, 1, out SalvageLedger.Entry _)) DroneChat.Skip(who, "1", null, prefix);
                else Msg.Tell(who, "Auto-Salvage has not taken anything yet - or the server has restarted since it did.");
            }
            if (Take(_owner, ListLastCVar)) DroneChat.Last(who, null, prefix);
            if (Take(_owner, UndoSkipCVar)) DroneChat.Undo(who, prefix);
        }

        /// <summary>
        /// True once per press. Always lowers a raised flag - through EntityAlive.SetCVar, which syncs
        /// the change to the client (see DronePatch.PublishOne for why not Buffs.SetCustomVar).
        /// </summary>
        private static bool Take(EntityPlayer _owner, string _cvar)
        {
            if (_owner.Buffs.GetCustomVar(_cvar) == 0f) return false;
            _owner.SetCVar(_cvar, 0f);

            ulong now = GameTimer.Instance.ticks;
            string key = _owner.entityId + ":" + _cvar;
            bool repeat = handledAt.TryGetValue(key, out ulong at) && now >= at && now - at < RepeatTicks;
            handledAt[key] = now;
            return !repeat;
        }
    }
}
