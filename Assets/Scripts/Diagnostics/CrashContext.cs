using Sentry.Unity;

namespace Multikulti.Diagnostics
{
    /// <summary>
    /// Game context attached to every crash report (SCRUM-100).
    /// The wave loop calls these on each state change, so a report says where in the game the crash happened.
    /// </summary>
    public static class CrashContext
    {
        /// <summary>Resort state from the build document §4.3, e.g. "AWAITING_WAVE", "WAVE_ACTIVE".</summary>
        public static void SetGameState(string state) =>
            SentrySdk.ConfigureScope(scope => scope.SetTag("game_state", state));

        /// <summary>Number of the current wave, 1-based.</summary>
        public static void SetWave(int waveIndex) =>
            SentrySdk.ConfigureScope(scope => scope.SetTag("wave_index", waveIndex.ToString()));
    }
}
