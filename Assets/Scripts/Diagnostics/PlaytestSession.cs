using System;
using UnityEngine;

namespace Multikulti.Diagnostics
{
    /// <summary>
    /// Identity of one game run (SCRUM-93, SCRUM-100).
    /// Playtest metrics and crash reports carry the same session_id, so a crash can be matched with the session it happened in.
    /// </summary>
    public static class PlaytestSession
    {
        static string s_Id;

        /// <summary>Random id created once per app launch.</summary>
        public static string Id => s_Id ??= Guid.NewGuid().ToString("N");

        /// <summary>Version of the build, e.g. "0.1.0" (Project Settings → Player → Version).</summary>
        public static string BuildVersion => Application.version;

        // Keeps a fresh id per Play mode run when Enter Play Mode options skip the domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => s_Id = null;
    }
}
