using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Diagnostics;
using Multikulti.Unlocks;
using Multikulti.Workers;
using UnityEngine;

namespace Multikulti.Waves
{
    /// <summary>
    /// Runs the wave state machine in the scene (SCRUM-26). One per scene.
    /// Reports every state change to crash reporting (SCRUM-100) and Start Wave to playtest metrics (SCRUM-93).
    ///
    /// Graybox behaviour until the related stories are done:
    /// WaveIncoming moves on to WaveActive after incomingSeconds, while ShipArrival sails the ship in (SCRUM-23),
    /// and WaveResolving moves on to WaveComplete in the next frame.
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
    /// Wave rating (SCRUM-43, SCRUM-44; state diagram "Vlna dokončená", entry / VypocitatRating): when the last unit
    /// is served the wave enters WaveResolving and VypocitatRating turns Waits into Wave Satisfaction
    /// (VypocitatSpokojnostHosti) and the rating 1★–5★ / Perfect; the result is the object Hodnotenie
    /// (HodnotenieVlny, activity diagram) and stays there until the next Start Wave.
    ///
    /// While WavePaused the buildings of the WorkerManager stop serving (Building.IsPaused) and WaveTime stops,
    /// so nothing advances and nothing gets worse (build document §4.3).
    ///
    /// Sudden rush (SCRUM-60): once, in wave sudden_rush.wave at sudden_rush.time_s of the arrival phase, extra_units
    /// of the need of the building that has no Worker arrive over duration_s on top of demand_total. The wave is not
    /// paused; SuddenRushPrompt offers Move Worker here / Ignore. The rush is plain extra demand: it is served and
    /// rated by waiting like every other unit and counts into the units of the wave.
    ///
    /// Pause and save (SCRUM-27, state diagram "Pozastavená"): entering WavePaused runs entry / UlozitStavVlny —
    /// the buildings stop and the whole wave is written to WaveSaveState; leaving it runs exit / ObnovitCasovac —
    /// the timers and buildings continue and the save is deleted. If the app is closed while paused, the next
    /// start finds the save, restores the wave into WavePaused and resumes it exactly where it stopped.
    ///
    /// LOCKED needs (SCRUM-34): a need whose building is not bought yet (Pool → Relax, Bungalows → Ocean View)
    /// gets demand 0 on Start Wave, so it releases no units. Buying is possible only between waves,
    /// so a need stays locked or unlocked for the whole wave.
    /// </summary>
    public sealed class WaveLoop : MonoBehaviour
    {
        [SerializeField, Min(0f)] float incomingSeconds = 2f;
        [SerializeField] WorkerManager workers;
        [SerializeField, Tooltip("Area unlocks (SCRUM-34); found in the scene when empty.")]
        AreaUnlockService unlocks;

        float m_StateTime;
        float m_WaveTime;
        float m_ArrivalTime;
        float m_DrainTime;
        bool m_ArrivalClosed;
        bool m_Restoring;
        bool m_ContinueSavedWave;
        readonly Dictionary<BuildingQueue, (int Arrived, int Served)> m_CountsAtWaveStart =
            new Dictionary<BuildingQueue, (int Arrived, int Served)>();
        bool m_RushDone;
        readonly HashSet<Need> m_MissingBuildings = new HashSet<Need>();
        readonly DemandRelease m_Release = new DemandRelease();
        readonly WaveWaitLog m_Waits = new WaveWaitLog();
        readonly List<BuildingQueue> m_ObservedQueues = new List<BuildingQueue>();

        public WaveStateMachine Machine { get; private set; }
        public WaveState State => Machine.State;
        public int Wave => Machine.Wave;

        /// <summary>Length of WaveIncoming (the ship sails in, SCRUM-23).</summary>
        public float IncomingSeconds => incomingSeconds;

        /// <summary>
        /// How far the ship got (SCRUM-23): 0 before Start Wave, 0 → 1 during WaveIncoming, 1 from WaveActive
        /// until the wave is over.
        /// </summary>
        public float IncomingProgress => Machine.State switch
        {
            WaveState.AwaitingWave => 0f,
            WaveState.WaveIncoming => incomingSeconds > 0f ? Mathf.Clamp01(m_StateTime / incomingSeconds) : 1f,
            _ => 1f
        };

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

        /// <summary>
        /// Wave Satisfaction of the last finished wave (SCRUM-43); calculated in entry / VypocitatRating (WaveResolving).
        /// Null before the first wave finishes and again from Start Wave until the wave is resolved.
        /// </summary>
        public WaveSatisfactionResult? Satisfaction { get; private set; }

        /// <summary>
        /// Result of the last finished wave: "hodnotenie : HodnotenieVlny" of the activity diagram (SCRUM-44).
        /// Created in entry / VypocitatRating; null from Start Wave until the wave is rated.
        /// </summary>
        public HodnotenieVlny Hodnotenie { get; private set; }

        /// <summary>Raised right after the wave is rated, for "Zobraziť hodnotenie vlny" (Wave Complete screen).</summary>
        public event Action<HodnotenieVlny> WaveRated;

        /// <summary>Seconds from the end of arrival until every building served all units (drain_s in playtest metrics).</summary>
        public float DrainSeconds => m_DrainTime;

        /// <summary>Raised once per wave when arrival closes and the drain starts.</summary>
        public event Action ArrivalClosed;

        /// <summary>Raised when a Sudden rush starts (SCRUM-60), with the building it hits.</summary>
        public event Action<Building> SuddenRushStarted;

        /// <summary>Building hit by the Sudden rush of the current wave (SCRUM-60); null when there is none.</summary>
        public Building SuddenRushBuilding { get; private set; }

        /// <summary>Buildings and Workers of the scene.</summary>
        public WorkerManager Workers => workers;

        /// <summary>
        /// SCRUM-34: the building of this need is not bought yet, so the need is LOCKED:
        /// no demand and not on the Wave Needs Panel. Without an AreaUnlockService nothing is locked.
        /// </summary>
        public bool IsNeedLocked(Need need) => unlocks != null && unlocks.IsNeedLocked(need);

        void Awake()
        {
            if (unlocks == null)
                unlocks = FindAnyObjectByType<AreaUnlockService>();
            Machine = new WaveStateMachine(WaveConfig.Current.Waves.Count);
            Machine.StateChanged += OnStateChanged;
            ReportState();
        }

        void Start()
        {
            SetBuildingsClock();
            ObserveBuildings();
            // Restored in the first frame, after every Start(): the Wave Needs Panel and others take their
            // Start Wave baseline in Start(), and the restored units of the wave must come on top of it.
            m_ContinueSavedWave = true;
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
            if (m_ContinueSavedWave)
            {
                m_ContinueSavedWave = false;
                TryContinueSavedWave();
            }

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
                TryStartSuddenRush();
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

        /// <summary>
        /// SCRUM-60: starts the Sudden rush once its time in the arrival phase has come. It hits the building
        /// without a Worker (in the prototype there is always exactly one), so moving a Worker there always helps.
        /// </summary>
        void TryStartSuddenRush()
        {
            if (m_RushDone) return;
            var rush = WaveConfig.Current.SuddenRush;
            if (rush.Wave != Machine.Wave || rush.ExtraUnits <= 0 || m_ArrivalTime < rush.TimeSeconds) return;
            m_RushDone = true;

            var target = BuildingWithoutWorker();
            if (target == null)
            {
                Debug.LogWarning($"Wave {Machine.Wave}: Sudden rush skipped, every building of the wave has a Worker.");
                return;
            }

            m_Release.AddRush(target.Need, rush.ExtraUnits, rush.TimeSeconds, rush.DurationSeconds);
            SuddenRushBuilding = target;
            Debug.Log($"Wave {Machine.Wave}: Sudden rush at {target.name}! +{rush.ExtraUnits} {target.Need} " +
                      $"over {rush.DurationSeconds:0} s (now {m_Release.Demand(target.Need)} in the wave)");
            SuddenRushStarted?.Invoke(target);
        }

        /// <summary>The building of an unlocked need that has no Worker; prefers one whose need is in the wave.</summary>
        Building BuildingWithoutWorker()
        {
            if (workers == null) return null;
            Building fallback = null;
            foreach (var building in workers.Buildings)
            {
                if (building == null || building.Queue == null || building.HasWorker || IsNeedLocked(building.Need))
                    continue;
                if (m_Release.Demand(building.Need) > 0) return building;
                fallback ??= building;
            }
            return fallback;
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
                m_RushDone = false;
                SuddenRushBuilding = null;
                m_Release.Begin(CurrentWave(), WaveTimer, IsNeedLocked);
                LogLockedNeeds();
                m_MissingBuildings.Clear();
                m_Waits.Clear();
                Satisfaction = null;
                Hodnotenie = null;
                SetBuildingsClock();
                ObserveBuildings();
                RememberCountsAtWaveStart();
                WaveSaveState.Delete();
                PlaytestMetrics.WaveStarted(Machine.Wave);
            }

            // State diagram "Pozastavená": entry / UlozitStavVlny, exit / ObnovitCasovac (SCRUM-27).
            if (next == WaveState.WavePaused)
                UlozitStavVlny();
            else if (previous == WaveState.WavePaused)
                ObnovitCasovac();

            // State diagram "Vlna dokončená": entry / VypocitatRating (SCRUM-44).
            if (next == WaveState.WaveResolving)
                VypocitatRating();

            ReportState();
            Debug.Log($"Wave {Machine.Wave}: {previous.ToJiraName()} → {next.ToJiraName()}");

            if (next == WaveState.WaveComplete)
                LogNextWave();
        }

        /// <summary>
        /// SCRUM-28: after a finished wave tells which scripted wave comes next and how large it is,
        /// or that all scripted waves are done.
        /// </summary>
        void LogNextWave()
        {
            var waves = WaveConfig.Current.Waves;
            var nextIndex = Machine.Wave; // Wave is 1-based, so the next wave's index equals the current number.
            if (nextIndex < waves.Count)
                Debug.Log($"Wave {Machine.Wave}/{Machine.WaveCount} complete, next wave {nextIndex + 1} " +
                          $"has {waves[nextIndex].TotalUnits} units");
            else
                Debug.Log($"Wave {Machine.Wave}/{Machine.WaveCount} complete, all scripted waves finished");
        }

        /// <summary>
        /// Forecast of the next wave (SCRUM-22): needs and volumes of the wave Start Wave would start, without LOCKED
        /// needs. Meant for AwaitingWave (shown in "Čaká na vlnu"); null when no wave is left.
        /// </summary>
        public WaveForecast NextWaveForecast() =>
            Machine.HasNextWave ? WaveForecast.For(CurrentWave(), IsNeedLocked) : null;

        WaveDefinition CurrentWave()
        {
            var waves = WaveConfig.Current.Waves;
            var index = Machine.Wave - 1;
            return index >= 0 && index < waves.Count ? waves[index] : null;
        }

        /// <summary>Logs the needs the wave would bring but which stay LOCKED (SCRUM-34).</summary>
        void LogLockedNeeds()
        {
            var wave = CurrentWave();
            if (wave == null) return;
            foreach (Need need in Enum.GetValues(typeof(Need)))
            {
                if (IsNeedLocked(need) && DemandRelease.DemandOf(wave, need) > 0)
                    Debug.Log($"Wave {Machine.Wave}: {need} LOCKED (building not bought), " +
                              $"its {DemandRelease.DemandOf(wave, need)} units do not come");
            }
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

        /// <summary>
        /// entry / VypocitatRating of "Vlna dokončená" (SCRUM-44, activity "Vypočítať rating"): calculates the
        /// satisfaction of the guests, turns it into 1★–5★ / Perfect by rating_thresholds and keeps the result
        /// as Hodnotenie (HodnotenieVlny).
        /// </summary>
        void VypocitatRating()
        {
            var satisfaction = VypocitatSpokojnostHosti();
            var rating = HodnotenieVlny.RatingFor(satisfaction.Value, WaveConfig.Current.Satisfaction.RatingThresholds);
            Hodnotenie = new HodnotenieVlny(Machine.Wave, satisfaction, rating);
            Debug.Log($"Wave {Machine.Wave}: rating {Hodnotenie.RatingLabel} (Wave Satisfaction {satisfaction.Value:P0})");
            WaveRated?.Invoke(Hodnotenie);
        }

        /// <summary>
        /// Use case "Vypočítať spokojnosť hostí" (SCRUM-43, included by "Zobraziť hodnotenie vlny"): turns the waits
        /// of the wave into Wave Satisfaction and logs it with the per-need breakdown.
        /// </summary>
        WaveSatisfactionResult VypocitatSpokojnostHosti()
        {
            var result = WaveSatisfaction.Calculate(m_Waits.All, WaveConfig.Current.Satisfaction);
            Satisfaction = result;

            var breakdown = new System.Text.StringBuilder();
            foreach (Need need in Enum.GetValues(typeof(Need)))
            {
                if (result.UnitsOf(need) == 0) continue;
                if (breakdown.Length > 0) breakdown.Append(", ");
                breakdown.Append($"{need} {result.Of(need):P0}");
            }
            var floor = result.IsOnWaveFloor ? $" (raw {result.RawValue:P0}, lifted to wave_floor)" : "";
            Debug.Log($"Wave {Machine.Wave}: Wave Satisfaction {result.Value:P0}{floor}, " +
                      $"{result.Units} units — {breakdown}");
            return result;
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

        /// <summary>
        /// entry / UlozitStavVlny (SCRUM-27): stops the buildings and saves the whole running wave, so it can
        /// continue even if the app is closed now.
        /// </summary>
        void UlozitStavVlny()
        {
            SetBuildingsPaused(true);
            if (m_Restoring) return;

            try
            {
                CaptureWave().Write();
                Debug.Log($"Wave {Machine.Wave}: paused, wave saved to {WaveSaveState.FilePath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Wave {Machine.Wave}: paused, but the wave could not be saved: {e.Message}");
            }
        }

        /// <summary>
        /// exit / ObnovitCasovac (SCRUM-27): the wave timers and the buildings continue exactly where they stopped;
        /// the save is no longer needed.
        /// </summary>
        void ObnovitCasovac()
        {
            SetBuildingsPaused(false);
            WaveSaveState.Delete();
            Debug.Log($"Wave {Machine.Wave}: resumed at {m_WaveTime:0.0} s of the wave");
        }

        WaveSaveState CaptureWave()
        {
            var state = new WaveSaveState
            {
                wave = Machine.Wave,
                wave_time = m_WaveTime,
                arrival_time = m_ArrivalTime,
                drain_time = m_DrainTime,
                arrival_closed = m_ArrivalClosed,
                rush_done = m_RushDone,
                release = m_Release.Capture()
            };

            if (workers != null)
            {
                state.worker_layout = workers.CaptureLayout();
                state.rush_building = SuddenRushBuilding != null ? IndexOfBuilding(SuddenRushBuilding) : -1;
                state.buildings = new BuildingSaveState[workers.Buildings.Count];
                for (var i = 0; i < workers.Buildings.Count; i++)
                {
                    var building = workers.Buildings[i];
                    var queue = building.Queue;
                    m_CountsAtWaveStart.TryGetValue(queue, out var atStart);
                    var seated = new List<UnitSaveState>();
                    foreach (var unit in queue.Seated)
                        seated.Add(new UnitSaveState
                        {
                            arrived_at = unit.ArrivedAt,
                            seated_at = unit.SeatedAt ?? unit.ArrivedAt,
                            progress = unit.ServiceProgress
                        });
                    var backlog = new List<UnitSaveState>();
                    foreach (var unit in queue.Backlog)
                        backlog.Add(new UnitSaveState { arrived_at = unit.ArrivedAt });

                    state.buildings[i] = new BuildingSaveState
                    {
                        need = (int)building.Need,
                        level = building.Level,
                        arrived = queue.ArrivedCount - atStart.Arrived,
                        served = queue.ServedCount - atStart.Served,
                        seated = seated.ToArray(),
                        backlog = backlog.ToArray()
                    };
                }
            }

            var waits = new List<UnitWaitSaveState>();
            foreach (var wait in m_Waits.All)
                waits.Add(new UnitWaitSaveState
                {
                    need = (int)wait.Need, arrived_at = wait.ArrivedAt, seated_at = wait.SeatedAt
                });
            state.waits = waits.ToArray();
            return state;
        }

        /// <summary>
        /// On start (SCRUM-27): when the app was closed during a paused wave, restores that wave into WavePaused
        /// and resumes it — the player comes back to the game ("Hráč sa vráti do hry").
        /// A save that does not fit the scene or the configuration is dropped and the game starts normally.
        /// </summary>
        void TryContinueSavedWave()
        {
            var state = WaveSaveState.Read();
            if (state == null) return;

            if (!FitsScene(state))
            {
                Debug.LogWarning("Wave save does not fit this scene or wave_config.json, starting without it.");
                WaveSaveState.Delete();
                return;
            }

            m_Restoring = true;
            try
            {
                RestoreWave(state);
                Machine.RestorePaused(state.wave);
            }
            finally
            {
                m_Restoring = false;
            }

            Debug.Log($"Wave {state.wave}: continued from the save at {m_WaveTime:0.0} s of the wave");
            Machine.Resume();
        }

        bool FitsScene(WaveSaveState state)
        {
            if (state.wave < 1 || state.wave > Machine.WaveCount || workers == null) return false;
            if (state.buildings.Length != workers.Buildings.Count) return false;
            for (var i = 0; i < state.buildings.Length; i++)
            {
                if (workers.Buildings[i] == null || (int)workers.Buildings[i].Need != state.buildings[i].need)
                    return false;
            }
            return true;
        }

        void RestoreWave(WaveSaveState state)
        {
            m_WaveTime = state.wave_time;
            m_ArrivalTime = state.arrival_time;
            m_DrainTime = state.drain_time;
            m_ArrivalClosed = state.arrival_closed;
            m_RushDone = state.rush_done;
            SuddenRushBuilding = state.rush_building >= 0 && state.rush_building < workers.Buildings.Count
                ? workers.Buildings[state.rush_building]
                : null;
            m_StateTime = 0f;
            m_Release.Restore(state.release);
            m_MissingBuildings.Clear();

            workers.RestoreLayout(state.worker_layout);
            for (var i = 0; i < state.buildings.Length; i++)
            {
                var saved = state.buildings[i];
                var building = workers.Buildings[i];
                while (building.Level < saved.level && !building.IsMaxLevel)
                    building.LevelUp();

                var need = building.Need;
                var seated = new List<Unit>();
                foreach (var unit in saved.seated)
                    seated.Add(new Unit(need, unit.arrived_at) { SeatedAt = unit.seated_at, ServiceProgress = unit.progress });
                var backlog = new List<Unit>();
                foreach (var unit in saved.backlog)
                    backlog.Add(new Unit(need, unit.arrived_at));
                building.Queue.Restore(saved.arrived, saved.served, seated, backlog);
            }
            RememberCountsAtWaveStart();

            m_Waits.Clear();
            foreach (var wait in state.waits)
                m_Waits.Restore(new UnitWait((Need)wait.need, wait.arrived_at, wait.seated_at));
        }

        int IndexOfBuilding(Building building)
        {
            for (var i = 0; i < workers.Buildings.Count; i++)
                if (workers.Buildings[i] == building) return i;
            return -1;
        }

        /// <summary>Counts of every building at Start Wave, so the save holds only the units of this wave.</summary>
        void RememberCountsAtWaveStart()
        {
            m_CountsAtWaveStart.Clear();
            if (workers == null) return;
            foreach (var building in workers.Buildings)
            {
                if (building != null && building.Queue != null)
                    m_CountsAtWaveStart[building.Queue] = (building.Queue.ArrivedCount, building.Queue.ServedCount);
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
