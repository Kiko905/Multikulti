using Sentry.Unity;
using UnityEngine;

namespace Multikulti.Diagnostics
{
    /// <summary>
    /// Crash reporting setup (SCRUM-100). The DSN lives in Assets/Resources/Sentry/SentryOptions.asset (Tools → Sentry);
    /// everything else is set here so it is reviewed in code, not hidden in an asset.
    /// Sentry calls Configure during the build and again when the game starts.
    /// </summary>
    [CreateAssetMenu(fileName = "MultikultiSentryConfig", menuName = "Multikulti/Sentry Config")]
    public sealed class MultikultiSentryConfig : SentryOptionsConfiguration
    {
        public override void Configure(SentryUnityOptions options)
        {
            options.Release = $"multikulti@{PlaytestSession.BuildVersion}";
            options.Environment = Application.isEditor ? "editor"
                : UnityEngine.Debug.isDebugBuild ? "development"
                : "playtest";

            // Playtesters stay anonymous: no IP address, no user names.
            options.SendDefaultPii = false;
            options.AttachScreenshot = true;
            options.Debug = UnityEngine.Debug.isDebugBuild;

            // Same session_id as the playtest metrics (SCRUM-93).
            options.DefaultTags["session_id"] = PlaytestSession.Id;
            options.DefaultTags["build_version"] = PlaytestSession.BuildVersion;
        }
    }
}
