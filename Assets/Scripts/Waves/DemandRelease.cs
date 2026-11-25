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
    ///
    /// Sudden rush (SCRUM-60): AddRush adds extra units of one need on top of demand_total, released evenly
    /// from the rush start over its duration. They count into Demand, so the wave ends only after they are served.
    /// </summary>
    public sealed class DemandRelease
    {
        static readonly Need[] k_Needs = (Need[])Enum.GetValues(typeof(Need));

        readonly int[] m_Demand = new int[k_Needs.Length];
        readonly int[] m_Released = new int[k_Needs.Length];
        readonly int[] m_Extra = new int[k_Needs.Length];
        readonly int[] m_ExtraReleased = new int[k_Needs.Length];
        readonly float[] m_ExtraStart = new float[k_Needs.Length];
        readonly float[] m_ExtraDuration = new float[k_Needs.Length];

        /// <summary>Length of the arrival phase the current wave was started with.</summary>
        public float WaveTimer { get; private set; }

        /// <summary>True when every unit of every need of the wave has been released.</summary>
        public bool IsComplete
        {
            get
            {
                for (var i = 0; i < k_Needs.Length; i++)
                {
                    if (m_Released[i] < m_Demand[i] || m_ExtraReleased[i] < m_Extra[i]) return false;
                }
                return true;
            }
        }

        /// <summary>All units of a need in the current wave: demand_total plus Sudden rush units (SCRUM-60).</summary>
        public int Demand(Need need) => m_Demand[(int)need] + m_Extra[(int)need];

        /// <summary>Sudden rush units of a need added to the current wave (SCRUM-60); 0 without a rush.</summary>
        public int ExtraDemand(Need need) => m_Extra[(int)need];

        /// <summary>Units of a need released so far in the current wave, Sudden rush units included.</summary>
        public int Released(Need need) => m_Released[(int)need] + m_ExtraReleased[(int)need];

        /// <summary>Release rate of a need in units per second (demand_total ÷ wave_timer).</summary>
        public float Rate(Need need) => WaveTimer > 0f ? m_Demand[(int)need] / WaveTimer : 0f;

        /// <summary>
        /// Prepares the release of a new wave. A null wave releases nothing.
        /// A need for which isLocked returns true (its building is not bought, SCRUM-34) gets demand 0,
        /// so it generates no units at all.
        /// </summary>
        public void Begin(WaveDefinition wave, float waveTimer, Func<Need, bool> isLocked = null)
        {
            if (waveTimer <= 0f) throw new ArgumentOutOfRangeException(nameof(waveTimer));
            WaveTimer = waveTimer;
            for (var i = 0; i < k_Needs.Length; i++)
            {
                var locked = isLocked != null && isLocked(k_Needs[i]);
                m_Demand[i] = wave != null && !locked ? Math.Max(0, DemandOf(wave, k_Needs[i])) : 0;
                m_Released[i] = 0;
                m_Extra[i] = 0;
                m_ExtraReleased[i] = 0;
                m_ExtraStart[i] = 0f;
                m_ExtraDuration[i] = 0f;
            }
        }

        /// <summary>Copies the release of the current wave for the wave save (SCRUM-27).</summary>
        public DemandReleaseState Capture() => new DemandReleaseState
        {
            wave_timer = WaveTimer,
            demand = (int[])m_Demand.Clone(),
            released = (int[])m_Released.Clone()
        };

        /// <summary>Continues the release exactly where Capture left it (SCRUM-27).</summary>
        public void Restore(DemandReleaseState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            WaveTimer = state.wave_timer;
            for (var i = 0; i < k_Needs.Length; i++)
            {
                m_Demand[i] = state.demand != null && i < state.demand.Length ? state.demand[i] : 0;
                m_Released[i] = state.released != null && i < state.released.Length ? state.released[i] : 0;
            }
        }

        /// <summary>
        /// Sudden rush (SCRUM-60): adds units of one need to the current wave. They arrive evenly from
        /// startTime (arrival time in seconds) over duration seconds; the first one comes at startTime.
        /// </summary>
        public void AddRush(Need need, int units, float startTime, float duration)
        {
            if (units <= 0) return;
            var i = (int)need;
            m_Extra[i] += units;
            m_ExtraStart[i] = startTime;
            m_ExtraDuration[i] = Math.Max(0.01f, duration);
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

                if (m_Extra[i] == 0) continue;
                var extraDue = DueCount(m_Extra[i], m_ExtraDuration[i], arrivalTime - m_ExtraStart[i]);
                while (m_ExtraReleased[i] < extraDue)
                {
                    m_ExtraReleased[i]++;
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

    /// <summary>Saved release of a wave (SCRUM-27), one value per Need.</summary>
    [Serializable]
    public sealed class DemandReleaseState
    {
        public float wave_timer;
        public int[] demand = Array.Empty<int>();
        public int[] released = Array.Empty<int>();
    }
}
