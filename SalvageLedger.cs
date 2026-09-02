using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// What the drone just took, so you can say "not that" after the fact.
    ///
    /// This exists because pointing at the thing you want spared does not work: by the time a
    /// player notices the drone ate the working vending machine, the vending machine is a pile of
    /// scrap and there is nothing left to aim at. The one thing they always have is the memory of
    /// it happening a second ago - so the interface is a log of the last few salvages, and an
    /// exclude that takes a line number from it.
    ///
    /// Kept in memory only. It answers "what did you just do", which is worthless after a restart,
    /// and the half that must persist - the rules, and which families have been met - lives in
    /// SalvageRules.
    /// </summary>
    public static class SalvageLedger
    {
        public struct Entry
        {
            public string BlockName;
            public string Family;
            public Vector3i Pos;
            public ulong Tick;
        }

        /// <summary>Deep enough to cover "it did it a minute ago", short enough to read in chat.</summary>
        public const int Size = 20;

        private static readonly Dictionary<int, List<Entry>> byOwner = new Dictionary<int, List<Entry>>();
        private static readonly object gate = new object();

        public static void Record(int _ownerId, string _blockName, Vector3i _pos)
        {
            if (_ownerId < 0 || string.IsNullOrEmpty(_blockName)) return;

            Entry e = new Entry
            {
                BlockName = _blockName,
                Family = SalvageRules.Family(_blockName),
                Pos = _pos,
                Tick = GameManager.Instance?.World?.worldTime ?? 0UL,
            };

            lock (gate)
            {
                if (!byOwner.TryGetValue(_ownerId, out List<Entry> list))
                {
                    list = new List<Entry>(Size);
                    byOwner[_ownerId] = list;
                }

                // Newest first: a player reading chat wants the thing that just happened at the top,
                // and "/das skip 1" should mean the most recent one.
                list.Insert(0, e);
                if (list.Count > Size) list.RemoveAt(list.Count - 1);
            }
        }

        /// <summary>The most recent entries for a player, newest first. Never null.</summary>
        public static List<Entry> Recent(int _ownerId, int _count)
        {
            lock (gate)
            {
                if (!byOwner.TryGetValue(_ownerId, out List<Entry> list)) return new List<Entry>();
                int n = Mathf.Clamp(_count, 1, list.Count);
                return list.GetRange(0, n);
            }
        }

        /// <summary>
        /// One entry by its 1-based position in what /das last printed, or false when the number is
        /// past the end of the log.
        /// </summary>
        public static bool ByIndex(int _ownerId, int _oneBased, out Entry _entry)
        {
            _entry = default;
            lock (gate)
            {
                if (!byOwner.TryGetValue(_ownerId, out List<Entry> list)) return false;
                if (_oneBased < 1 || _oneBased > list.Count) return false;
                _entry = list[_oneBased - 1];
                return true;
            }
        }

        public static void Clear(int _ownerId)
        {
            lock (gate) byOwner.Remove(_ownerId);
        }
    }
}
