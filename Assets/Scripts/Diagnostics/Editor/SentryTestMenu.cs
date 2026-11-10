using System;
using Sentry.Unity;
using UnityEditor;
using UnityEngine;

namespace Multikulti.Diagnostics.EditorTools
{
    /// <summary>Checks that crash reporting works (SCRUM-100). Use in Play mode, then look for the events on sentry.io.</summary>
    static class SentryTestMenu
    {
        [MenuItem("Multikulti/Sentry/Send test message")]
        static void SendTestMessage()
        {
            if (!RequirePlayMode())
                return;
            SentrySdk.CaptureMessage("SCRUM-100 test message");
            Debug.Log("Sentry: test message sent.");
        }

        [MenuItem("Multikulti/Sentry/Send test exception")]
        static void SendTestException()
        {
            if (!RequirePlayMode())
                return;
            CrashContext.SetGameState("WAVE_ACTIVE");
            CrashContext.SetWave(2);
            Debug.LogException(new InvalidOperationException("SCRUM-100 test exception"));
        }

        static bool RequirePlayMode()
        {
            if (EditorApplication.isPlaying)
                return true;
            Debug.LogWarning("Sentry test: start Play mode first, the SDK only runs while the game runs.");
            return false;
        }
    }
}
