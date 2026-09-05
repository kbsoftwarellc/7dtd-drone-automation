using System;
using System.Collections.Generic;

namespace DroneAutomation
{
    /// <summary>
    /// Talking to one player.
    ///
    /// Chat is the whole interface a server-side mod gets. A proper window would be nicer, but a
    /// client mod's XUi is discarded the moment you join a dedicated server - the client re-parses
    /// the SERVER's copy, which never saw the patch - so a window means shipping a DLL to every
    /// player and turning EAC off for all of them. Chat reaches an unmodified client with nothing
    /// installed, which is worth more than a prettier panel.
    /// </summary>
    public static class Msg
    {
        public static void Tell(int _entityId, string _line)
        {
            if (_entityId < 0 || string.IsNullOrEmpty(_line)) return;
            try
            {
                GameManager.Instance.ChatMessageServer(
                    null, EChatType.Global, -1, _line, new List<int> { _entityId }, EMessageSender.Server);
            }
            catch (Exception e)
            {
                Log.Warning("[DroneAutomation] Could not send chat: " + e.Message);
            }
        }

        public static void TellAll(int _entityId, IEnumerable<string> _lines)
        {
            if (_lines == null) return;
            foreach (string line in _lines) Tell(_entityId, line);
        }
    }
}
