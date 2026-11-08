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
    /// While WavePaused the buildings of the WorkerManager stop serving (Building.IsPaused) and WaveTime stops,
    /// so nothing advances and nothing gets worse (build document §4.3).
    /// </summary>
    public sealed class WaveLoop : MonoBehaviour
    {
        [SerializeField, Min(0f)] float incomingSeconds = 2f;
        [SerializeField] WorkerManager workers;

        float m_StateTime;
        float m_WaveTime;

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

        void Awake()
        {
            Machine = new WaveStateMachine(WaveConfig.Current.Waves.Count);
            Machine.StateChanged += OnStateChanged;
            ReportState();
        }

        void OnDestroy()
        {
            if (Machine != null)
                Machine.StateChanged -= OnStateChanged;
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
                case WaveState.WaveResolving:
                    Machine.Complete();
                    break;
            }
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
                PlaytestMetrics.WaveStarted(Machine.Wave);
            }

            if (next == WaveState.WavePaused || previous == WaveState.WavePaused)
                SetBuildingsPaused(next == WaveState.WavePaused);

            ReportState();
            Debug.Log($"Wave {Machine.Wave}: {previous.ToJiraName()} → {next.ToJiraName()}");
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
            GUI.Label(new Rect(10, 10, 400, 24), label);
        }
#endif
    }
}
