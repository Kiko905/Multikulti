using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Waves;

namespace Multikulti.Unlocks
{
    public enum AreaUnlockState { Locked, Available, Bought }

    public readonly struct AreaUnlock
    {
        public readonly string Area;
        public readonly Need Need;
        public readonly int AfterWave;
        public readonly int Price;

        public AreaUnlock(string area, Need need, int afterWave, int price)
        {
            Area = area;
            Need = need;
            AfterWave = afterWave;
            Price = price;
        }
    }

    public sealed class AreaUnlocks
    {
        readonly List<AreaUnlock> m_Unlocks = new List<AreaUnlock>();
        readonly HashSet<Need> m_Bought = new HashSet<Need>();

        public AreaUnlocks(IEnumerable<AreaUnlock> unlocks)
        {
            foreach (var unlock in unlocks)
            {
                if (Contains(unlock.Need))
                    throw new ArgumentException($"Duplicate area unlock for {unlock.Need}.", nameof(unlocks));
                m_Unlocks.Add(unlock);
            }
        }

        public IReadOnlyList<AreaUnlock> All => m_Unlocks;

        public bool Contains(Need need) => TryGet(need, out _);

        public bool TryGet(Need need, out AreaUnlock unlock)
        {
            foreach (var candidate in m_Unlocks)
            {
                if (candidate.Need != need) continue;
                unlock = candidate;
                return true;
            }
            unlock = default;
            return false;
        }

        public AreaUnlock Get(Need need)
        {
            if (!TryGet(need, out var unlock))
                throw new ArgumentException($"No area unlock for {need}.", nameof(need));
            return unlock;
        }

        public bool IsBought(Need need) => m_Bought.Contains(need);

        /// <summary>
        /// SCRUM-34: a need is LOCKED while its area unlock is not bought (also while it is Available).
        /// Needs without an area unlock (Rooms, Drinks, Food) are never locked.
        /// </summary>
        public bool IsNeedLocked(Need need) => Contains(need) && !IsBought(need);

        public bool SetBought(Need need, bool bought)
        {
            Get(need);
            return bought ? m_Bought.Add(need) : m_Bought.Remove(need);
        }

        public AreaUnlockState StateOf(Need need, int completedWaves)
        {
            var unlock = Get(need);
            if (m_Bought.Contains(need)) return AreaUnlockState.Bought;
            return completedWaves >= unlock.AfterWave ? AreaUnlockState.Available : AreaUnlockState.Locked;
        }

        public bool CanOpen(Need need, int completedWaves, WaveState state) =>
            state == WaveState.AwaitingWave && StateOf(need, completedWaves) == AreaUnlockState.Available;

        public static int CompletedWaves(int wave, WaveState state) =>
            state == WaveState.WaveResolving || state == WaveState.WaveComplete || state == WaveState.Upgrade
                ? wave
                : wave - 1;
    }
}
