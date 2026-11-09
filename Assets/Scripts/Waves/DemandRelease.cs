using System;
using Multikulti.Buildings;
using Multikulti.Config;

namespace Multikulti.Waves
{
    /// <summary>
    /// Even release of demand during the arrival phase (SCRUM-24, build document §4.4).
    /// Every need of the wave is released at demand_total ÷ wave_timer units per second:
    /// unit i (0-based) of a need arrives at i × wave_timer ÷ demand_total seconds of the arrival phase,
    /// the same times tools/wave_sim.py uses. The first unit comes right when the arrival starts.
    ///
    /// Pure C#: WaveLoop calls Begin on Start Wave, Advance every frame of the arrival phase with the
    /// arrival time (it stops while WavePaused) and ReleaseRemaining when arrival closes.
    /// </summary>
    public sealed class DemandRelease
    {
        static readonly Need[] k_Needs = (Need[])Enum.GetValues(typeof(Need));

        readonly int[] m_Demand = new int[k_Needs.Length];
        readonly int[] m_Released = new int[k_Needs.Length];

        /// <summary>Length of the arrival phase the current wave was started with.</summary>
        public float WaveTimer { get; private set; }

        /// <summary>True when every unit of every need of the wave has been released.</summary>
        public bool IsComplete
        {
            get
            {
                for (var i = 0; i < k_Needs.Length; i++)
                {
                    if (m_Released[i] < m_Demand[i]) return false;
                }
                return true;
            }
        }

        /// <summary>demand_total of a need in the current wave.</summary>
        public int Demand(Need need) => m_Demand[(int)need];

        /// <summary>Units of a need released so far in the current wave.</summary>
        public int Released(Need need) => m_Released[(int)need];

        /// <summary>Release rate of a need in units per second (demand_total ÷ wave_timer).</summary>
        public float Rate(Need need) => WaveTimer > 0f ? m_Demand[(int)need] / WaveTimer : 0f;

        /// <summary>Prepares the release of a new wave. A null wave releases nothing.</summary>
        public void Begin(WaveDefinition wave, float waveTimer)
        {
            if (waveTimer <= 0f) throw new ArgumentOutOfRangeException(nameof(waveTimer));
            WaveTimer = waveTimer;
            for (var i = 0; i < k_Needs.Length; i++)
            {
                m_Demand[i] = wave != null ? Math.Max(0, DemandOf(wave, k_Needs[i])) : 0;
                m_Released[i] = 0;
            }
        }

        /// <summary>
        /// Releases every unit that is due at arrivalTime (seconds since the arrival phase started).
        /// Several units of one need can come in one frame when the frame is long.
        /// </summary>
        public void Advance(float arrivalTime, Action<Need> release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            for (var i = 0; i < k_Needs.Length; i++)
            {
                var due = DueCount(m_Demand[i], WaveTimer, arrivalTime);
                while (m_Released[i] < due)
                {
                    m_Released[i]++;
                    release(k_Needs[i]);
                }
            }
        }

        /// <summary>
        /// Releases the units not released yet, so the wave always brings its whole demand_total.
        /// Normally nothing is left when wave_timer elapses; units are left only when arrival is closed early (debug key L).
        /// </summary>
        public void ReleaseRemaining(Action<Need> release) => Advance(float.PositiveInfinity, release);

        /// <summary>
        /// Units of a need that have arrived by arrivalTime: unit i is due at i × waveTimer ÷ demand,
        /// so the count is floor(arrivalTime × demand ÷ waveTimer) + 1, at most demand.
        /// </summary>
        public static int DueCount(int demand, float waveTimer, float arrivalTime)
        {
            if (demand <= 0 || arrivalTime < 0f) return 0;
            if (arrivalTime >= waveTimer) return demand;
            var due = (int)Math.Floor(arrivalTime * demand / waveTimer) + 1;
            return Math.Min(demand, due);
        }

        /// <summary>demand_total of a need in a scripted wave; 0 means the need is not in that wave.</summary>
        public static int DemandOf(WaveDefinition wave, Need need) => need switch
        {
            Need.Rooms => wave.Rooms,
            Need.Drinks => wave.Drinks,
            Need.Food => wave.Food,
            Need.Relax => wave.Relax,
            Need.OceanView => wave.OceanView,
            _ => 0
        };
    }
}
