using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Config;

namespace Multikulti.Waves
{
    /// <summary>Wave Satisfaction of a finished wave (SCRUM-43): the whole wave and the average per need.</summary>
    public readonly struct WaveSatisfactionResult
    {
        static readonly int k_NeedCount = Enum.GetValues(typeof(Need)).Length;

        readonly float[] m_ByNeed;
        readonly int[] m_UnitsByNeed;

        public WaveSatisfactionResult(float value, float rawValue, int units, float[] byNeed, int[] unitsByNeed)
        {
            Value = value;
            RawValue = rawValue;
            Units = units;
            m_ByNeed = byNeed;
            m_UnitsByNeed = unitsByNeed;
        }

        /// <summary>Wave Satisfaction 0–1 after wave_floor; this is the number the rating uses (SCRUM-44).</summary>
        public float Value { get; }

        /// <summary>Weighted average of the unit contributions before wave_floor (for logs and playtest metrics).</summary>
        public float RawValue { get; }

        /// <summary>Number of units that were counted.</summary>
        public int Units { get; }

        /// <summary>True when wave_floor lifted the result (every unit waited too long).</summary>
        public bool IsOnWaveFloor => Value > RawValue;

        /// <summary>Number of counted units of one need.</summary>
        public int UnitsOf(Need need) => m_UnitsByNeed != null ? m_UnitsByNeed[(int)need] : 0;

        /// <summary>
        /// Average contribution 0–1 of the units of one need, without weights and floors of the wave.
        /// 1 when the need had no units. For the "why" breakdown on Wave Complete.
        /// </summary>
        public float Of(Need need) =>
            m_ByNeed != null && UnitsOf(need) > 0 ? m_ByNeed[(int)need] : 1f;

        internal static int NeedCount => k_NeedCount;
    }

    /// <summary>
    /// Wave Satisfaction (SCRUM-43, build document §4.4). Pure C#, so it can be tested without Unity.
    ///
    /// Each unit of a need contributes by its waiting (arrival → seated, service not counted):
    ///   waiting ≤ patience_grace_s              → 100 %
    ///   patience_grace_s … patience_floor_after_s → falls linearly 100 % → unit_floor
    ///   waiting ≥ patience_floor_after_s        → unit_floor
    ///
    /// Wave Satisfaction = max(wave_floor, Σ(contribution × weight) ÷ Σ weight),
    /// weight 1.0 for volume needs and ocean_view_weight for Ocean View.
    /// It is calculated once from the history of the wave, never from the state at its end.
    /// </summary>
    public static class WaveSatisfaction
    {
        /// <summary>Contribution 0–1 of one unit that waited the given seconds.</summary>
        public static float UnitContribution(float waitingSeconds, SatisfactionSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return UnitContribution(waitingSeconds, settings.PatienceGraceSeconds,
                settings.PatienceFloorAfterSeconds, settings.UnitFloor);
        }

        /// <summary>Contribution 0–1 of one unit; see the class summary for the curve.</summary>
        public static float UnitContribution(float waitingSeconds, float graceSeconds, float floorAfterSeconds, float unitFloor)
        {
            if (waitingSeconds <= graceSeconds) return 1f;
            if (waitingSeconds >= floorAfterSeconds) return unitFloor;
            var t = (waitingSeconds - graceSeconds) / (floorAfterSeconds - graceSeconds);
            return 1f + (unitFloor - 1f) * t;
        }

        /// <summary>Weight of a need in the wave average: Ocean View weighs ocean_view_weight, the rest 1.0.</summary>
        public static float Weight(Need need, SatisfactionSettings settings) =>
            need == Need.OceanView ? settings.OceanViewWeight : 1f;

        /// <summary>Calculates Wave Satisfaction from the waits of every unit seated in the wave.</summary>
        public static WaveSatisfactionResult Calculate(IReadOnlyList<UnitWait> waits, SatisfactionSettings settings)
        {
            if (waits == null) throw new ArgumentNullException(nameof(waits));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var needCount = WaveSatisfactionResult.NeedCount;
            var sumByNeed = new float[needCount];
            var unitsByNeed = new int[needCount];
            var weightedSum = 0f;
            var weightTotal = 0f;

            foreach (var wait in waits)
            {
                var contribution = UnitContribution(wait.WaitingSeconds, settings);
                var weight = Weight(wait.Need, settings);
                weightedSum += contribution * weight;
                weightTotal += weight;
                sumByNeed[(int)wait.Need] += contribution;
                unitsByNeed[(int)wait.Need]++;
            }

            for (var i = 0; i < needCount; i++)
            {
                if (unitsByNeed[i] > 0)
                    sumByNeed[i] /= unitsByNeed[i];
            }

            // A wave in which nobody waited (or nobody came) is not a bad wave.
            var raw = weightTotal > 0f ? weightedSum / weightTotal : 1f;
            var value = Math.Max(settings.WaveFloor, raw);
            return new WaveSatisfactionResult(value, raw, waits.Count, sumByNeed, unitsByNeed);
        }
    }
}
