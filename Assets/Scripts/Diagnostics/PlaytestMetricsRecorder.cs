using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Economy;
using Multikulti.Unlocks;
using Multikulti.Waves;
using Multikulti.Workers;
using UnityEngine;

namespace Multikulti.Diagnostics
{
    /// <summary>
    /// Playtest metrics instrumentation (SCRUM-103, build document §4.12): listens to the game and writes the events
    /// of PlaytestMetrics (SCRUM-93), so wave_complete_rate, worker_move_before_prompt_rate, worker_moves_pre_wave /
    /// worker_moves_in_wave, drain_duration_s, rating_distribution, upgrade_take_rate and session_length can be
    /// evaluated with tools/playtest_report.py. Energy usage is deliberately not measured.
    ///
    /// Only listens to events of the game; it changes nothing in it. Wave started is written by WaveLoop itself,
    /// session start, app pause and session end by PlaytestMetrics.
    ///
    /// - worker_moved: every Worker move by the player (not the new Worker that comes with Pool / Bungalows). "pre_wave" while AwaitingWave carries the number of the wave that is coming
    ///   (the preparation by the forecast), "in_wave" carries the running wave; target_need_state is the state of the
    ///   target building's need at the moment of the move.
    /// - bottleneck_shown: a need enters BOTTLENECK (the queue-growing warning and toast appear).
    /// - wave_completed: on WAVE_COMPLETE with rating, Wave Satisfaction, drain and wave duration (without pauses).
    /// - upgrade_offered: on UPGRADE the recommended upgrade per AC6 — the building whose need was in BOTTLENECK
    ///   the longest in the wave; nothing is offered after a wave without a bottleneck or when that building is at max level.
    /// - upgrade_bought, area_unlock_bought: the purchases; wave = the last finished wave.
    ///
    /// Building names follow Docs/Slovnik.md (Reception, BeachBar, Restaurant, Pool, Bungalows).
    /// </summary>
    public sealed class PlaytestMetricsRecorder : MonoBehaviour
    {
        [SerializeField, Tooltip("Found in the scene when empty.")] WaveLoop waveLoop;
        [SerializeField, Tooltip("Found in the scene when empty.")] WorkerManager workers;
        [SerializeField, Tooltip("Found in the scene when empty.")] NeedStateBoard needStates;
        [SerializeField, Tooltip("Found in the scene when empty.")] UpgradeService upgrades;
        [SerializeField, Tooltip("Found in the scene when empty.")] AreaUnlockService unlocks;

        readonly Dictionary<Need, float> m_BottleneckSince = new Dictionary<Need, float>();
        readonly Dictionary<Need, float> m_BottleneckSeconds = new Dictionary<Need, float>();
        WorkerRoster m_Roster;
        bool m_Subscribed;

        void Awake()
        {
            if (waveLoop == null) waveLoop = FindAnyObjectByType<WaveLoop>();
            if (workers == null) workers = FindAnyObjectByType<WorkerManager>();
            if (needStates == null) needStates = FindAnyObjectByType<NeedStateBoard>();
            if (upgrades == null) upgrades = FindAnyObjectByType<UpgradeService>();
            if (unlocks == null) unlocks = FindAnyObjectByType<AreaUnlockService>();
        }

        // Start: WaveLoop.Machine and WorkerManager.Roster are created in their Awake.
        void Start()
        {
            if (waveLoop == null || waveLoop.Machine == null)
            {
                Debug.LogWarning("Playtest metrics: no WaveLoop in the scene, nothing is recorded.", this);
                enabled = false;
                return;
            }

            waveLoop.Machine.StateChanged += OnWaveStateChanged;
            m_Roster = workers != null ? workers.Roster : null;
            if (m_Roster != null) m_Roster.WorkerMoved += OnWorkerMoved;
            if (needStates != null) needStates.StateChanged += OnNeedStateChanged;
            if (upgrades != null) upgrades.Upgraded += OnUpgraded;
            if (unlocks != null) unlocks.Bought += OnAreaUnlockBought;
            m_Subscribed = true;
        }

        void OnDestroy()
        {
            if (!m_Subscribed) return;
            if (waveLoop != null && waveLoop.Machine != null) waveLoop.Machine.StateChanged -= OnWaveStateChanged;
            if (m_Roster != null) m_Roster.WorkerMoved -= OnWorkerMoved;
            if (needStates != null) needStates.StateChanged -= OnNeedStateChanged;
            if (upgrades != null) upgrades.Upgraded -= OnUpgraded;
            if (unlocks != null) unlocks.Bought -= OnAreaUnlockBought;
        }

        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            switch (next)
            {
                case WaveState.WaveIncoming:
                    m_BottleneckSince.Clear();
                    m_BottleneckSeconds.Clear();
                    break;
                case WaveState.WaveComplete:
                    CloseBottlenecks();
                    WriteWaveCompleted();
                    break;
                case WaveState.Upgrade:
                    WriteUpgradeOffered();
                    break;
            }
        }

        void OnWorkerMoved(int worker, int from, int to)
        {
            // from < 0: a new Worker that came with a bought building (Pool, Bungalows), not a move by the player.
            if (from < 0) return;

            var target = BuildingAt(to);
            var targetState = target != null && needStates != null ? needStates.StateOf(target.Need) : NeedState.Ok;
            PlaytestMetrics.WorkerMoved(waveLoop.Wave, waveLoop.State.IsInWave(),
                NameOf(BuildingAt(from)), NameOf(target), targetState.ToJiraName());
        }

        void OnNeedStateChanged(Need need, NeedState previous, NeedState next)
        {
            var now = waveLoop.WaveTime;
            if (next == NeedState.Bottleneck)
            {
                m_BottleneckSince[need] = now;
                PlaytestMetrics.BottleneckShown(waveLoop.Wave, need.ToString());
            }
            else if (previous == NeedState.Bottleneck && m_BottleneckSince.TryGetValue(need, out var since))
            {
                AddBottleneckTime(need, now - since);
                m_BottleneckSince.Remove(need);
            }
        }

        void WriteWaveCompleted()
        {
            var result = waveLoop.Hodnotenie;
            if (result == null) return;
            PlaytestMetrics.WaveCompleted(result.Wave, result.RatingLabel, result.Satisfaction.Value,
                waveLoop.DrainSeconds, waveLoop.WaveTime);
        }

        /// <summary>AC6: the recommended upgrade is the building of the need that was in BOTTLENECK the longest.</summary>
        void WriteUpgradeOffered()
        {
            var building = RecommendedUpgrade();
            if (building == null) return;
            PlaytestMetrics.UpgradeOffered(waveLoop.Wave, NameOf(building));
        }

        Building RecommendedUpgrade()
        {
            var longest = 0f;
            Need? need = null;
            foreach (var pair in m_BottleneckSeconds)
            {
                if (pair.Value <= longest) continue;
                longest = pair.Value;
                need = pair.Key;
            }
            if (need == null || workers == null) return null;

            foreach (var building in workers.Buildings)
            {
                if (building != null && building.Need == need.Value && !building.IsMaxLevel)
                    return building;
            }
            return null;
        }

        void OnUpgraded(Building building) =>
            PlaytestMetrics.UpgradeBought(LastFinishedWave(), NameOf(building), building.Level);

        void OnAreaUnlockBought(AreaUnlock unlock) =>
            PlaytestMetrics.AreaUnlockBought(LastFinishedWave(), NameOf(unlock.Need));

        /// <summary>A need still in BOTTLENECK when the wave ends counts until the end of the wave.</summary>
        void CloseBottlenecks()
        {
            var now = waveLoop.WaveTime;
            foreach (var pair in m_BottleneckSince)
                AddBottleneckTime(pair.Key, now - pair.Value);
            m_BottleneckSince.Clear();
        }

        void AddBottleneckTime(Need need, float seconds)
        {
            m_BottleneckSeconds.TryGetValue(need, out var total);
            m_BottleneckSeconds[need] = total + Mathf.Max(0f, seconds);
        }

        /// <summary>Wave a purchase belongs to: the finished one; while AwaitingWave Wave is already the next one.</summary>
        int LastFinishedWave() =>
            waveLoop.State == WaveState.AwaitingWave ? Mathf.Max(0, waveLoop.Wave - 1) : waveLoop.Wave;

        Building BuildingAt(int index) =>
            workers != null && index >= 0 && index < workers.Buildings.Count ? workers.Buildings[index] : null;

        static string NameOf(Building building) => building != null ? NameOf(building.Need) : null;

        /// <summary>Code name of the building of a need, Docs/Slovnik.md.</summary>
        static string NameOf(Need need) => need switch
        {
            Need.Rooms => "Reception",
            Need.Drinks => "BeachBar",
            Need.Food => "Restaurant",
            Need.Relax => "Pool",
            Need.OceanView => "Bungalows",
            _ => need.ToString()
        };
    }
}
