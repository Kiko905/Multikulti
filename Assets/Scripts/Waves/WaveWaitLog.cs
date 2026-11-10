using System;
using System.Collections.Generic;
using Multikulti.Buildings;

namespace Multikulti.Waves
{
    /// <summary>Waiting of one unit of the wave: arrival → seated, the service is not counted (SCRUM-42).</summary>
    public readonly struct UnitWait
    {
        public UnitWait(Need need, float arrivedAt, float seatedAt)
        {
            Need = need;
            ArrivedAt = arrivedAt;
            SeatedAt = seatedAt;
        }

        public Need Need { get; }

        /// <summary>WaveTime when the unit arrived.</summary>
        public float ArrivedAt { get; }

        /// <summary>WaveTime when the unit got a seat and its service started.</summary>
        public float SeatedAt { get; }

        /// <summary>Seconds the unit waited for a seat.</summary>
        public float WaitingSeconds => Math.Max(0f, SeatedAt - ArrivedAt);
    }

    /// <summary>
    /// Waiting of every unit of the current wave (SCRUM-42, build document §4.4).
    /// WaveLoop records a unit when a building seats it, so the time of the service itself never counts.
    /// The times use WaveTime, which stops while WavePaused, so a pause adds no waiting.
    /// Wave Satisfaction (SCRUM-43) turns these waits into unit contributions (patience_grace_s, patience_floor_after_s).
    ///
    /// Pure C#: Clear on Start Wave, Record for every seated unit.
    /// </summary>
    public sealed class WaveWaitLog
    {
        static readonly int k_NeedCount = Enum.GetValues(typeof(Need)).Length;

        readonly List<UnitWait> m_All = new List<UnitWait>();
        readonly List<float>[] m_ByNeed = new List<float>[k_NeedCount];

        public WaveWaitLog()
        {
            for (var i = 0; i < k_NeedCount; i++)
                m_ByNeed[i] = new List<float>();
        }

        /// <summary>Every unit seated in the current wave, in the order they were seated.</summary>
        public IReadOnlyList<UnitWait> All => m_All;

        /// <summary>Number of units seated in the current wave.</summary>
        public int Count => m_All.Count;

        /// <summary>Waits of the seated units of one need, in seconds, in the order they were seated.</summary>
        public IReadOnlyList<float> Waits(Need need) => m_ByNeed[(int)need];

        /// <summary>Average waiting of one need in seconds; 0 when no unit of it was seated.</summary>
        public float Average(Need need)
        {
            var waits = m_ByNeed[(int)need];
            if (waits.Count == 0) return 0f;
            var sum = 0f;
            foreach (var wait in waits) sum += wait;
            return sum / waits.Count;
        }

        /// <summary>Longest waiting of one need in seconds; 0 when no unit of it was seated.</summary>
        public float Max(Need need)
        {
            var max = 0f;
            foreach (var wait in m_ByNeed[(int)need])
                max = Math.Max(max, wait);
            return max;
        }

        /// <summary>Forgets the previous wave. Call on Start Wave.</summary>
        public void Clear()
        {
            m_All.Clear();
            foreach (var waits in m_ByNeed) waits.Clear();
        }

        /// <summary>Records a unit that was just seated. A unit without SeatedAt is ignored.</summary>
        public void Record(Unit unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (!unit.SeatedAt.HasValue) return;

            var entry = new UnitWait(unit.Need, unit.ArrivedAt, unit.SeatedAt.Value);
            m_All.Add(entry);
            m_ByNeed[(int)unit.Need].Add(entry.WaitingSeconds);
        }
    }
}
