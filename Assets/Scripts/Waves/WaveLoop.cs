using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Diagnostics;
using Multikulti.Workers;
using UnityEngine;

namespace Multikulti.Waves
{
    /// <summary>
    /// Runs the wave state machine in the scene (SCRUM-26). One per scene.
    /// Reports every state change to crash reporting (SCRUM-100) and Start Wave to playtest metrics (SCRUM-93).
    ///
    /// Graybox behaviour until the related stories are done:
    /// WaveIncoming moves on to WaveActive after incomingSeconds (the ship animation comes with SCRUM-23),
    /// and WaveResolving moves on to WaveComplete in the next frame (the result comes with SCRUM-43).
    ///
    /// Arrival and drain (SCRUM-25): wave_timer seconds after WaveActive starts, arrival closes and no new
    /// guests come; the wave keeps running until every building has served all its units (Queue.IsAllServed),
    /// only then it moves on to WaveResolving. wave_timer alone never ends the wave.
    ///
    /// Demand release (SCRUM-24): during the arrival phase DemandRelease sends the units of every need of the wave
    /// evenly at demand_total ÷ wave_timer to the building of that need. Arrival and seating times of the units
    /// use WaveTime (every building's Clock is set to it).
    ///
    /// Waiting (SCRUM-42): when a building seats a unit during the wave, its waiting (arrival → seated) goes
    /// into Waits; the service time is not counted. Waits is cleared on Start Wave and summarised in the log
    /// when the wave resolves.
    ///
    /// While WavePaused the buildings of the WorkerManager stop serving (Building.IsPaused) and WaveTime stops,
    /// so nothing advances and nothing gets worse (build document §4.3).
    /// </summary>
    public sealed class WaveLoop : MonoBehaviour
    {
        [SerializeField, Min(0f)] float incomingSeconds = 2f;
        [SerializeField] WorkerManager workers;

        float m_StateTime;
        float m_WaveTime;
        float m_ArrivalTime;
        float m_DrainTime;
        bool m_ArrivalClosed;
        readonly HashSet<Need> m_MissingBuildings = new HashSet<Need>();
        readonly DemandRelease m_Release = new DemandRelease();
        readonly WaveWaitLog m_Waits = new WaveWaitLog();
        readonly List<BuildingQueue> m_ObservedQueues = new List<BuildingQueue>();

        public WaveStateMachine Machine { get; private set; }
        public WaveState State => Machine.State;
        public int Wave => Machine.Wave;

        /// <summary>Seconds spent in the current state; the time stops while WavePaused.</summary>
        public float StateTime => m_StateTime;

        /// <summary>
        /// Seconds since Start Wave of the current wave; stops while WavePaused.
        /// Use it as "now" for Unit arrival and seating times (SCRUM-24, SCRUM-42) so a pause adds no waiting.
        /// </summary>
        public float WaveTime => m_WaveTime;

        /// <summary>True while the wave is paused; WorkerManager.CanMove() reads it (SCRUM-37).</summary>
        public bool IsPaused => Machine.State == WaveState.WavePaused;

        /// <summary>Length of the arrival phase from wave_config.json (wave.wave_timer).</summary>
        public float WaveTimer => WaveConfig.Current.Wave.WaveTimer;

        /// <summary>Seconds of the arrival phase so far (from WaveActive start); stops while WavePaused.</summary>
        public float ArrivalTime => m_ArrivalTime;

        /// <summary>
        /// True while new guests may arrive: WaveActive and wave_timer not elapsed yet.
        /// The demand release (SCRUM-24) checks this before it sends a unit.
        /// </summary>
        public bool IsArrivalOpen => Machine.State == WaveState.WaveActive && !m_ArrivalClosed;

        /// <summary>True after wave_timer elapsed in the current wave (the drain phase is running or done).</summary>
        public bool IsArrivalClosed => m_ArrivalClosed;

        /// <summary>Demand of the current wave and how much of it has arrived (SCRUM-24).</summary>
        public DemandRelease Release => m_Release;

        /// <summary>Waiting of every unit seated in the current wave (SCRUM-42).</summary>
        public WaveWaitLog Waits => m_Waits;

        /// <summary>Seconds from the end of arrival until every building served all units (drain_s in playtest metrics).</summary>
        public float DrainSeconds => m_DrainTime;

        /// <summary>Raised once per wave when arrival closes and the drain starts.</summary>
        public event Action ArrivalClosed;

        void Awake()
        {
            Machine = new WaveStateMachine(WaveConfig.Current.Waves.Count);
            Machine.StateChanged += OnStateChanged;
            ReportState();
        }

        void Start()
        {
            SetBuildingsClock();
            ObserveBuildings();
        }

        void OnDestroy()
        {
            if (Machine != null)
                Machine.StateChanged -= OnStateChanged;
            foreach (var queue in m_ObservedQueues)
                queue.UnitSeated -= OnUnitSeated;
            m_ObservedQueues.Clear();
        }

        void Update()
        {
            if (Machine.State == WaveState.WavePaused) return;
            m_StateTime += Time.deltaTime;
            if (Machine.State.IsInWave()) m_WaveTime += Time.deltaTime;

            switch (Machine.State)
            {
                case WaveState.WaveIncoming when m_StateTime >= incomingSeconds:
                    Machine.GuestsArrived();
                    break;
                case WaveState.WaveActive:
                    TickActive(Time.deltaTime);
                    break;
                case WaveState.WaveResolving:
                    Machine.Complete();
                    break;
            }
        }

        void TickActive(float deltaTime)
        {
            if (!m_ArrivalClosed)
            {
                m_ArrivalTime += deltaTime;
                m_Release.Advance(m_ArrivalTime, SendUnit);
                if (m_ArrivalTime >= WaveTimer) CloseArrival();
                return;
            }

            m_DrainTime += deltaTime;
            if (AllBuildingsServed())
            {
                Debug.Log($"Wave {Machine.Wave}: all units served, drain {m_DrainTime:0.0} s");
                LogWaits();
                Machine.Resolve();
            }
        }

        /// <summary>
        /// Closes arrival: no new guests come, buildings drain their queues. Called when wave_timer elapses;
        /// public so the debug input can skip the arrival phase. Does nothing outside WaveActive or if already closed.
        /// </summary>
        public void CloseArrival()
        {
            if (Machine.State != WaveState.WaveActive || m_ArrivalClosed) return;
            m_ArrivalClosed = true;
            m_Release.ReleaseRemaining(SendUnit);
            Debug.Log($"Wave {Machine.Wave}: arrival closed after {m_ArrivalTime:0.0} s, drain starts");
            ArrivalClosed?.Invoke();
        }

        /// <summary>True when no building has a unit seated or waiting (backlog 0 for every need).</summary>
        public bool AllBuildingsServed()
        {
            if (workers == null) return true;
            foreach (var building in workers.Buildings)
            {
                if (building.Queue != null && !building.Queue.IsAllServed)
                    return false;
            }
            return true;
        }

        void OnApplicationPause(bool paused)
        {
            if (Machine == null) return;
            if (paused) Machine.Pause();
            else Machine.Resume();
        }

        void OnStateChanged(WaveState previous, WaveState next)
        {
            // Returning from a pause continues the same state, so its time is kept.
            if (previous != WaveState.WavePaused && next != WaveState.WavePaused)
                m_StateTime = 0f;

            if (next == WaveState.WaveIncoming)
            {
                m_WaveTime = 0f;
                m_ArrivalTime = 0f;
                m_DrainTime = 0f;
                m_ArrivalClosed = false;
                m_Release.Begin(CurrentWave(), WaveTimer);
                m_MissingBuildings.Clear();
                m_Waits.Clear();
                SetBuildingsClock();
                ObserveBuildings();
                PlaytestMetrics.WaveStarted(Machine.Wave);
            }

            if (next == WaveState.WavePaused || previous == WaveState.WavePaused)
                SetBuildingsPaused(next == WaveState.WavePaused);

            ReportState();
            Debug.Log($"Wave {Machine.Wave}: {previous.ToJiraName()} → {next.ToJiraName()}");
        }

        WaveDefinition CurrentWave()
        {
            var waves = WaveConfig.Current.Waves;
            var index = Machine.Wave - 1;
            return index >= 0 && index < waves.Count ? waves[index] : null;
        }

        /// <summary>
        /// Sends one arriving unit to the building of its need (SCRUM-24). With several buildings of one need
        /// the unit goes to the one with the fewest pending units.
        /// </summary>
        void SendUnit(Need need)
        {
            Building target = null;
            if (workers != null)
            {
                foreach (var building in workers.Buildings)
                {
                    if (building == null || building.Need != need || building.Queue == null) continue;
                    if (target == null || building.Queue.PendingCount < target.Queue.PendingCount)
                        target = building;
                }
            }

            if (target == null)
            {
                if (m_MissingBuildings.Add(need))
                    Debug.LogWarning($"Wave {Machine.Wave}: no building for {need}, its units are not sent.");
                return;
            }

            target.Queue.Enqueue(new Unit(need, m_WaveTime), m_WaveTime);
        }

        /// <summary>Puts every building on WaveTime, so arrival and seating use one clock (SCRUM-24).</summary>
        void SetBuildingsClock()
        {
            if (workers == null) return;
            foreach (var building in workers.Buildings)
            {
                if (building != null)
                    building.Clock = () => m_WaveTime;
            }
        }

        /// <summary>Listens to UnitSeated of every building once; buildings added later join on the next Start Wave.</summary>
        void ObserveBuildings()
        {
            if (workers == null) return;
            foreach (var building in workers.Buildings)
            {
                var queue = building != null ? building.Queue : null;
                if (queue == null || m_ObservedQueues.Contains(queue)) continue;
                queue.UnitSeated += OnUnitSeated;
                m_ObservedQueues.Add(queue);
            }
        }

        void OnUnitSeated(Unit unit)
        {
            if (Machine.State.IsInWave())
                m_Waits.Record(unit);
        }

        void LogWaits()
        {
            foreach (Need need in Enum.GetValues(typeof(Need)))
            {
                var waits = m_Waits.Waits(need);
                if (waits.Count == 0) continue;
                Debug.Log($"Wave {Machine.Wave}: {need} waiting avg {m_Waits.Average(need):0.0} s, " +
                          $"max {m_Waits.Max(need):0.0} s ({waits.Count} units)");
            }
        }

        void SetBuildingsPaused(bool paused)
        {
            if (workers == null) return;
            foreach (var building in workers.Buildings)
                building.IsPaused = paused;
        }

        void ReportState()
        {
            CrashContext.SetGameState(Machine.State.ToJiraName());
            CrashContext.SetWave(Machine.Wave);
        }

#if UNITY_EDITOR
        void OnGUI()
        {
            if (Machine == null) return;
            var label = Machine.HasNextWave || Machine.State != WaveState.AwaitingWave
                ? $"Wave {Machine.Wave}/{Machine.WaveCount}: {Machine.State.ToJiraName()} ({m_StateTime:0.0} s)"
                : $"All {Machine.WaveCount} waves finished";
            if (Machine.State == WaveState.WaveActive || Machine.State == WaveState.WavePaused)
                label += m_ArrivalClosed
                    ? $" — drain {m_DrainTime:0.0} s"
                    : $" — arrival {m_ArrivalTime:0}/{WaveTimer:0} s";
            GUI.Label(new Rect(10, 10, 500, 24), label);
        }
#endif
    }
}
