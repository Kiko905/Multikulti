using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Config;

namespace Multikulti.Waves
{
    /// <summary>
    /// Forecast of the next wave (SCRUM-22, build document §4.3, UI handoff S1): while AwaitingWave the player sees
    /// which needs come in the next wave and in what volume ("Next wave: Rooms 16 · Drinks 18 · Food 14") and moves
    /// Workers by it before Start Wave.
    ///
    /// Pure C#. The volumes are the demand_total values of the scripted wave, the same numbers DemandRelease.Begin
    /// releases: a LOCKED need (building not bought yet, SCRUM-34) is left out because it brings no units.
    /// After buying Pool or Bungalows the forecast shows the new need right away (AC7a).
    /// The Sudden rush (SCRUM-60) is a surprise and is never part of the forecast.
    /// </summary>
    public sealed class WaveForecast
    {
        static readonly Need[] k_Needs = (Need[])Enum.GetValues(typeof(Need));

        readonly List<NeedForecast> m_Needs;

        WaveForecast(int wave, List<NeedForecast> needs)
        {
            Wave = wave;
            m_Needs = needs;
        }

        /// <summary>Number of the forecast wave (1-based).</summary>
        public int Wave { get; }

        /// <summary>Needs that come in the wave, in the order of Need, each with its volume (at least 1).</summary>
        public IReadOnlyList<NeedForecast> Needs => m_Needs;

        /// <summary>All units of the wave.</summary>
        public int TotalUnits
        {
            get
            {
                var total = 0;
                foreach (var need in m_Needs) total += need.Units;
                return total;
            }
        }

        /// <summary>Volume of one need in the forecast; 0 when it does not come.</summary>
        public int UnitsOf(Need need)
        {
            foreach (var forecast in m_Needs)
                if (forecast.Need == need) return forecast.Units;
            return 0;
        }

        /// <summary>
        /// Forecast of a scripted wave. isLocked tells which needs are LOCKED now (null = nothing locked);
        /// a null wave (no next wave) gives null.
        /// </summary>
        public static WaveForecast For(WaveDefinition wave, Func<Need, bool> isLocked = null)
        {
            if (wave == null) return null;
            var needs = new List<NeedForecast>();
            foreach (var need in k_Needs)
            {
                if (isLocked != null && isLocked(need)) continue;
                var units = DemandRelease.DemandOf(wave, need);
                if (units > 0) needs.Add(new NeedForecast(need, units));
            }
            return new WaveForecast(wave.Wave, needs);
        }
    }

    /// <summary>One need of the forecast with its volume.</summary>
    public readonly struct NeedForecast
    {
        public NeedForecast(Need need, int units)
        {
            Need = need;
            Units = units;
        }

        public Need Need { get; }
        public int Units { get; }
    }
}
