using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.UI
{
    /// <summary>Resort status on the Home screen (SCRUM-21, UI handoff S1: Ready for Guests / Wave in progress).</summary>
    public enum ResortStatus
    {
        /// <summary>AwaitingWave and a next wave exists: Start Wave is available.</summary>
        ReadyForGuests,
        /// <summary>WaveIncoming, WaveActive or WaveResolving.</summary>
        WaveInProgress,
        /// <summary>WavePaused: the app was in the background.</summary>
        WavePaused,
        /// <summary>WaveComplete or Upgrade: result and upgrade after the wave.</summary>
        WaveComplete,
        /// <summary>AwaitingWave after the last scripted wave.</summary>
        AllWavesDone
    }

    public static class ResortStatusRules
    {
        public static ResortStatus For(WaveState state, bool hasNextWave) => state switch
        {
            WaveState.AwaitingWave => hasNextWave ? ResortStatus.ReadyForGuests : ResortStatus.AllWavesDone,
            WaveState.WavePaused => ResortStatus.WavePaused,
            WaveState.WaveComplete or WaveState.Upgrade => ResortStatus.WaveComplete,
            _ => ResortStatus.WaveInProgress
        };

        /// <summary>Key in the Localization table UI.</summary>
        public static string LabelKey(this ResortStatus status) => status switch
        {
            ResortStatus.ReadyForGuests => "home.status.ready",
            ResortStatus.WavePaused => "home.status.paused",
            ResortStatus.WaveComplete => "home.status.complete",
            ResortStatus.AllWavesDone => "home.status.finished",
            _ => "home.status.wave"
        };

        public static Color BackgroundColor(this ResortStatus status) => status switch
        {
            ResortStatus.ReadyForGuests => new Color(0.16f, 0.55f, 0.32f, 0.95f),
            ResortStatus.WaveInProgress => new Color(0.85f, 0.42f, 0.12f, 0.95f),
            ResortStatus.WaveComplete => new Color(0.2f, 0.4f, 0.75f, 0.95f),
            _ => new Color(0.3f, 0.3f, 0.3f, 0.95f)
        };
    }
}
