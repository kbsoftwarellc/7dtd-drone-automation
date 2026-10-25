using System.Collections.Generic;

namespace DroneAutomation
{
    /// <summary>
    /// The drone speaking up the first time it meets a kind of thing.
    ///
    /// This is the other half of the after-the-fact problem SalvageLedger describes. A log only
    /// helps a player who already knows to go looking; the announcement is what turns a surprise
    /// into a prompt at the moment it happens - "I took one of these, say the word and I never will
    /// again". Once per family per player, so it teaches without becoming spam.
    /// </summary>
    public static class SalvageVoice
    {
        // What has already been said this session, per player entity. Not persisted: a player who
        // logs in after a restart and watches the drone stand still deserves to be told why again.
        private static readonly HashSet<string> heldAnnounced = new HashSet<string>();
        private static readonly HashSet<int> idleAnnounced = new HashSet<int>();
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
                if (!heldAnnounced.Add(_owner.entityId + ":" + family.ToLowerInvariant())) return;
            }

            Msg.Tell(_owner.entityId,
                $"Auto-Salvage found {_blockName} and left it standing (new target). " +
                $"{_prefix} allow {family}* to let it work on these, or {_prefix} rules to see the list.");
        }

        /// <summary>
        /// Says, once a session, that the drone is passing things by because its owner has not
        /// picked anything for it to take. Auto-Salvage starts empty (see SalvageCategories), and a
        /// drone with the module fitted that never wrenches anything reads as broken unless it says
        /// why - and says where the menu is, since nobody goes looking for a submenu they don't know
        /// exists. Only fired next to a block it could otherwise have taken, so it means something.
        /// </summary>
        public static void Idle(EntityPlayer _owner, string _blockName, string _prefix)
        {
            if (_owner == null) return;
            lock (gate)
            {
                if (!idleAnnounced.Add(_owner.entityId)) return;
            }

            Msg.Tell(_owner.entityId,
                $"Auto-Salvage left {_blockName} alone: you have not picked anything for it to take yet. " +
                "Talk to your drone > Automation modules > Auto-Salvage to choose. " +
                $"({_prefix} rules shows what is picked.)");
        }

        /// <summary>Forgets that a family was announced, so an allow/deny change can be re-reported.</summary>
        public static void ForgetHeld(int _entityId, string _family)
        {
            if (string.IsNullOrEmpty(_family)) return;
            lock (gate) heldAnnounced.Remove(_entityId + ":" + _family.ToLowerInvariant());
        }
    }
}
