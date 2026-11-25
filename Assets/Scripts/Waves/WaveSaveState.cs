using System;
using System.IO;
using UnityEngine;

namespace Multikulti.Waves
{
    /// <summary>
    /// Saved state of a paused wave (SCRUM-27, state diagram: Pozastavená, entry / UlozitStavVlny).
    /// WaveLoop writes it when the wave enters WavePaused (the app goes to the background — the last moment
    /// a mobile OS guarantees before it may close the app) and deletes it on exit / ObnovitCasovac.
    /// When the app starts and the file exists, the wave was closed while paused and WaveLoop continues it
    /// exactly from this state.
    ///
    /// Saves only the running wave (its timers, demand release, queues, waits and the Workers' layout);
    /// Coins, Energy, upgrades and unlocks are not part of the wave.
    /// </summary>
    [Serializable]
    public sealed class WaveSaveState
    {
        public const string FileName = "wave_save.json";
        public const int SupportedVersion = 1;

        public int version = SupportedVersion;
        public int wave;
        public float wave_time;
        public float arrival_time;
        public float drain_time;
        public bool arrival_closed;
        /// <summary>The Sudden rush of this wave already started (SCRUM-60); it must not start again after a restore.</summary>
        public bool rush_done;
        /// <summary>Index of the building hit by the Sudden rush; -1 when there is none.</summary>
        public int rush_building = -1;
        public DemandReleaseState release = new DemandReleaseState();
        public int[] worker_layout = Array.Empty<int>();
        public BuildingSaveState[] buildings = Array.Empty<BuildingSaveState>();
        public UnitWaitSaveState[] waits = Array.Empty<UnitWaitSaveState>();

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists => File.Exists(FilePath);

        public void Write() => File.WriteAllText(FilePath, JsonUtility.ToJson(this));

        /// <summary>Reads the saved wave; null when there is none or it cannot be read.</summary>
        public static WaveSaveState Read()
        {
            if (!Exists) return null;
            try
            {
                var state = JsonUtility.FromJson<WaveSaveState>(File.ReadAllText(FilePath));
                return state != null && state.version == SupportedVersion ? state : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Wave save: cannot read {FilePath}: {e.Message}");
                return null;
            }
        }

        public static void Delete()
        {
            if (Exists) File.Delete(FilePath);
        }
    }

    /// <summary>One building of a saved wave: its level and the units of the wave it has not served yet.</summary>
    [Serializable]
    public sealed class BuildingSaveState
    {
        public int need;
        public int level;
        public int arrived;
        public int served;
        public UnitSaveState[] seated = Array.Empty<UnitSaveState>();
        public UnitSaveState[] backlog = Array.Empty<UnitSaveState>();
    }

    [Serializable]
    public sealed class UnitSaveState
    {
        public float arrived_at;
        public float seated_at;
        public float progress;
    }

    [Serializable]
    public sealed class UnitWaitSaveState
    {
        public int need;
        public float arrived_at;
        public float seated_at;
    }
}
