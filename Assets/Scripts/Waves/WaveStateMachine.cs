using System;

namespace Multikulti.Waves
{
    /// <summary>
    /// Wave loop state machine (SCRUM-26, build document §4.3):
    /// AwaitingWave → WaveIncoming → WaveActive → WaveResolving → WaveComplete → Upgrade → AwaitingWave,
    /// plus WaveActive ⇄ WavePaused while the app is in the background.
    ///
    /// Plain C# without Unity, so it can be tested on its own. It only guards which transitions are legal;
    /// the conditions come from outside: the Start Wave button (SCRUM-23), the end of arrival and the drain
    /// (SCRUM-24, SCRUM-25), the result and upgrade screens. An illegal transition throws, so a wrong call
    /// order shows up immediately instead of silently breaking the loop.
    /// </summary>
    public sealed class WaveStateMachine
    {
        /// <summary>Current state.</summary>
        public WaveState State { get; private set; } = WaveState.AwaitingWave;

        /// <summary>
        /// Number of the wave, 1-based: the running wave, or the next one while AwaitingWave.
        /// Grows by 1 when the loop returns to AwaitingWave.
        /// </summary>
        public int Wave { get; private set; } = 1;

        /// <summary>Number of scripted waves (8 in the prototype).</summary>
        public int WaveCount { get; }

        /// <summary>False after the last scripted wave; Start Wave is then not offered.</summary>
        public bool HasNextWave => Wave <= WaveCount;

        /// <summary>Raised after every state change with the previous and the new state.</summary>
        public event Action<WaveState, WaveState> StateChanged;

        public WaveStateMachine(int waveCount)
        {
            if (waveCount <= 0) throw new ArgumentOutOfRangeException(nameof(waveCount));
            WaveCount = waveCount;
        }

        /// <summary>True if Start Wave may be offered right now.</summary>
        public bool CanStartWave => State == WaveState.AwaitingWave && HasNextWave;

        /// <summary>Player tapped Start Wave: AwaitingWave → WaveIncoming.</summary>
        public void StartWave()
        {
            if (!HasNextWave)
                throw new InvalidOperationException($"All {WaveCount} waves are finished.");
            MoveTo(WaveState.WaveIncoming, WaveState.AwaitingWave);
        }

        /// <summary>The ship has arrived and the guests have spawned: WaveIncoming → WaveActive.</summary>
        public void GuestsArrived() => MoveTo(WaveState.WaveActive, WaveState.WaveIncoming);

        /// <summary>
        /// App went to the background. Pauses a running wave (WaveActive → WavePaused) and returns true;
        /// in any other state there is nothing to freeze, so it returns false and the state stays.
        /// </summary>
        public bool Pause()
        {
            if (State != WaveState.WaveActive) return false;
            MoveTo(WaveState.WavePaused, WaveState.WaveActive);
            return true;
        }

        /// <summary>App came back: WavePaused → WaveActive, the wave continues exactly where it stopped. Returns false if nothing was paused.</summary>
        public bool Resume()
        {
            if (State != WaveState.WavePaused) return false;
            MoveTo(WaveState.WaveActive, WaveState.WavePaused);
            return true;
        }

        /// <summary>
        /// Arrival is closed (wave_timer elapsed) and every need has backlog 0: WaveActive → WaveResolving.
        /// The caller checks both conditions (SCRUM-25); wave_timer alone never ends the wave.
        /// </summary>
        public void Resolve() => MoveTo(WaveState.WaveResolving, WaveState.WaveActive);

        /// <summary>The wave result is ready and the result screen opens: WaveResolving → WaveComplete.</summary>
        public void Complete() => MoveTo(WaveState.WaveComplete, WaveState.WaveResolving);

        /// <summary>Player tapped Claim on the result screen: WaveComplete → Upgrade.</summary>
        public void Claim() => MoveTo(WaveState.Upgrade, WaveState.WaveComplete);

        /// <summary>Player confirmed or skipped the upgrade: Upgrade → AwaitingWave, the next wave becomes available.</summary>
        public void FinishUpgrade()
        {
            if (State != WaveState.Upgrade) ThrowIllegal(WaveState.AwaitingWave);
            Wave++;
            MoveTo(WaveState.AwaitingWave, WaveState.Upgrade);
        }

        void MoveTo(WaveState next, WaveState requiredCurrent)
        {
            if (State != requiredCurrent) ThrowIllegal(next);
            var previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }

        void ThrowIllegal(WaveState next) =>
            throw new InvalidOperationException(
                $"Illegal wave transition {State.ToJiraName()} → {next.ToJiraName()} (wave {Wave}).");
    }
}
