using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Multikulti.Diagnostics
{
    /// <summary>
    /// Playtest metrics (SCRUM-93, build document §4.12).
    /// Every event is one JSON line in Application.persistentDataPath/playtest/&lt;date&gt;_&lt;session_id&gt;.jsonl.
    /// Nothing leaves the device and no personal data is written; after a playtest the files are collected
    /// and evaluated with tools/playtest_report.py. Energy usage is deliberately not measured (§4.12).
    /// The wave loop calls the methods below; see Docs/playtest-metriky.md for where each one belongs.
    /// </summary>
    public static class PlaytestMetrics
    {
        public const string FolderName = "playtest";

        static string s_FilePath;
        static float s_SessionStart;
        static bool s_Failed;

        /// <summary>Folder with the metric files on this device.</summary>
        public static string Folder => Path.Combine(Application.persistentDataPath, FolderName);

        /// <summary>File of the current session, or null before the first event.</summary>
        public static string CurrentFile => s_FilePath;

        // ---------------------------------------------------------------- events

        /// <summary>Start Wave tapped (AwaitingWave → WaveIncoming).</summary>
        public static void WaveStarted(int wave) =>
            Write("wave_started", e => e.Field("wave", wave));

        /// <summary>
        /// A Worker moved between buildings.
        /// inWave: false while AwaitingWave (preparation by forecast), true during the wave.
        /// targetNeedState: state of the target building's need at that moment ("OK", "BOTTLENECK", "RECOVERING", ...).
        /// </summary>
        public static void WorkerMoved(int wave, bool inWave, string fromBuilding, string toBuilding, string targetNeedState) =>
            Write("worker_moved", e => e
                .Field("wave", wave)
                .Field("phase", inWave ? "in_wave" : "pre_wave")
                .Field("from", fromBuilding)
                .Field("to", toBuilding)
                .Field("target_need_state", targetNeedState));

        /// <summary>A need entered BOTTLENECK and the warning was shown to the player.</summary>
        public static void BottleneckShown(int wave, string need) =>
            Write("bottleneck_shown", e => e.Field("wave", wave).Field("need", need));

        /// <summary>
        /// Wave Complete screen reached.
        /// rating: "1★" … "5★" or "Perfect". satisfaction: 0–1. drainSeconds: time after wave_timer until all queues were empty.
        /// </summary>
        public static void WaveCompleted(int wave, string rating, float satisfaction, float drainSeconds, float durationSeconds) =>
            Write("wave_completed", e => e
                .Field("wave", wave)
                .Field("rating", rating)
                .Field("satisfaction", satisfaction)
                .Field("drain_s", drainSeconds)
                .Field("duration_s", durationSeconds));

        /// <summary>The Upgrade screen offered an upgrade.</summary>
        public static void UpgradeOffered(int wave, string building) =>
            Write("upgrade_offered", e => e.Field("wave", wave).Field("building", building));

        /// <summary>The player bought an upgrade.</summary>
        public static void UpgradeBought(int wave, string building, int newLevel) =>
            Write("upgrade_bought", e => e.Field("wave", wave).Field("building", building).Field("level", newLevel));

        /// <summary>The player bought an Area Unlock (Pool, Bungalows).</summary>
        public static void AreaUnlockBought(int wave, string building) =>
            Write("area_unlock_bought", e => e.Field("wave", wave).Field("building", building));

        // ---------------------------------------------------------------- session

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_FilePath = null;
            s_Failed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartSession()
        {
            s_SessionStart = Time.realtimeSinceStartup;
            Write("session_started", e => e
                .Field("platform", Application.platform.ToString())
                .Field("device_model", SystemInfo.deviceModel));

            Application.quitting += () => Write("session_ended", e => e);
            Application.focusChanged += focused => Write(focused ? "app_resumed" : "app_paused", e => e);
        }

        static void Write(string eventName, Func<EventWriter, EventWriter> fields)
        {
            if (s_Failed)
                return;

            try
            {
                if (s_FilePath == null)
                {
                    Directory.CreateDirectory(Folder);
                    var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    s_FilePath = Path.Combine(Folder, $"{date}_{PlaytestSession.Id}.jsonl");
                }

                var writer = new EventWriter()
                    .Field("event", eventName)
                    .Field("session_id", PlaytestSession.Id)
                    .Field("build_version", PlaytestSession.BuildVersion)
                    .Field("t", Time.realtimeSinceStartup - s_SessionStart)
                    .Field("ts", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

                File.AppendAllText(s_FilePath, fields(writer).ToJson() + "\n", Encoding.UTF8);
            }
            catch (Exception exception)
            {
                // Metrics must never break the game; report once and stop writing.
                s_Failed = true;
                Debug.LogWarning($"Playtest metrics disabled: {exception.Message}");
            }
        }

        /// <summary>Builds one flat JSON object without allocating a dictionary.</summary>
        sealed class EventWriter
        {
            readonly StringBuilder m_Json = new StringBuilder("{");

            public EventWriter Field(string key, string value)
            {
                Key(key);
                if (value == null)
                {
                    m_Json.Append("null");
                    return this;
                }

                m_Json.Append('"');
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '"': m_Json.Append("\\\""); break;
                        case '\\': m_Json.Append("\\\\"); break;
                        case '\n': m_Json.Append("\\n"); break;
                        case '\r': m_Json.Append("\\r"); break;
                        case '\t': m_Json.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                                m_Json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                m_Json.Append(c);
                            break;
                    }
                }
                m_Json.Append('"');
                return this;
            }

            public EventWriter Field(string key, int value)
            {
                Key(key);
                m_Json.Append(value.ToString(CultureInfo.InvariantCulture));
                return this;
            }

            public EventWriter Field(string key, float value)
            {
                Key(key);
                m_Json.Append(float.IsFinite(value) ? value.ToString("0.###", CultureInfo.InvariantCulture) : "null");
                return this;
            }

            public string ToJson() => m_Json.Append('}').ToString();

            void Key(string key)
            {
                if (m_Json.Length > 1)
                    m_Json.Append(',');
                m_Json.Append('"').Append(key).Append("\":");
            }
        }
    }
}
