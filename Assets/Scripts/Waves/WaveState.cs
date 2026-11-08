namespace Multikulti.Waves
{
    /// <summary>
    /// States of the wave loop (SCRUM-26, build document §4.3). Names follow Docs/Slovnik.md.
    /// The resting state is AwaitingWave; the banned term from SCRUM-91 is never used for it.
    /// </summary>
    public enum WaveState
    {
        /// <summary>Between waves: the player sees the forecast, moves Workers and buys upgrades.</summary>
        AwaitingWave,
        /// <summary>Ship arrives and guests spawn (transient, about 2–4 s).</summary>
        WaveIncoming,
        /// <summary>Arrival (0 → wave_timer) and then drain until every backlog is 0.</summary>
        WaveActive,
        /// <summary>App in the background: all timers frozen, nothing advances.</summary>
        WavePaused,
        /// <summary>Arrival closed and every need has backlog 0; the wave result is calculated.</summary>
        WaveResolving,
        /// <summary>Result screen: rating, rewards, breakdown.</summary>
        WaveComplete,
        /// <summary>Optional recommended upgrade; the player confirms or skips it.</summary>
        Upgrade
    }

    public static class WaveStateNames
    {
        /// <summary>Name as written in Jira and in the build document, e.g. "WAVE_ACTIVE" (used by crash reports).</summary>
        public static string ToJiraName(this WaveState state) => state switch
        {
            WaveState.AwaitingWave => "AWAITING_WAVE",
            WaveState.WaveIncoming => "WAVE_INCOMING",
            WaveState.WaveActive => "WAVE_ACTIVE",
            WaveState.WavePaused => "WAVE_PAUSED",
            WaveState.WaveResolving => "WAVE_RESOLVING",
            WaveState.WaveComplete => "WAVE_COMPLETE",
            WaveState.Upgrade => "UPGRADE",
            _ => state.ToString()
        };

        /// <summary>True while a wave is running: guests arrive or are being served.</summary>
        public static bool IsInWave(this WaveState state) =>
            state == WaveState.WaveIncoming || state == WaveState.WaveActive || state == WaveState.WavePaused;
    }
}
