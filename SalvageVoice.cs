using System.Collections.Generic;

namespace DroneAutomation
{
    /// <summary>
    /// The drone speaking up the first time it meets a kind of thing.
    ///
    /// This is the other half of the after-the-fact problem SalvageLedger describes. A log only
    /// helps a player who already knows to go looking; the announcement is what turns a surprise
    /// into a prompt at the moment it happens - "I took one of these, say the word and I never will
    /// again". Once per family per save, so it teaches without becoming spam.
    /// </summary>
    public static class SalvageVoice
    {
        // Families already announced as held back this session. Not persisted: a player who logs in
        // after a restart and watches the drone stand still deserves to be told why again.
        private static readonly HashSet<string> heldAnnounced = new HashSet<string>();
        private static readonly object gate = new object();

        /// <summary>Says what it took, the first time it takes one of these. See SalvageRules.MarkSeen.</summary>
        public static void FirstTake(EntityPlayer _owner, string _blockName, string _prefix)
        {
            if (_owner == null) return;
            string family = SalvageRules.Family(_blockName);
            Msg.Tell(_owner.entityId,
                $"Auto-Salvage took {_blockName} (first one). {_prefix} skip 1 to leave {family}* alone from now on.");
        }

        /// <summary>
        /// Says what it is refusing to touch under NewTargetPolicy="ask", once per family. The
        /// alternative is silence, which reads as a broken drone.
        /// </summary>
        public static void HeldBack(EntityPlayer _owner, string _blockName, string _prefix)
        {
            if (_owner == null) return;

            string family = SalvageRules.Family(_blockName);
            lock (gate)
            {
                if (!heldAnnounced.Add(family.ToLowerInvariant())) return;
            }

            Msg.Tell(_owner.entityId,
                $"Auto-Salvage found {_blockName} and left it standing (new target). " +
                $"{_prefix} allow {family}* to let it work on these, or {_prefix} rules to see the list.");
        }

        /// <summary>Forgets that a family was announced, so an allow/deny change can be re-reported.</summary>
        public static void ForgetHeld(string _family)
        {
            if (string.IsNullOrEmpty(_family)) return;
            lock (gate) heldAnnounced.Remove(_family.ToLowerInvariant());
        }
    }
}
