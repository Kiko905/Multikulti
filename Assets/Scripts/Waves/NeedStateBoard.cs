using System;
using System.Collections.Generic;
using System.Text;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Workers;
using UnityEngine;

namespace Multikulti.Waves
{
    public sealed class NeedStateBoard : MonoBehaviour
    {
        [SerializeField] WaveLoop waveLoop;
        [SerializeField] WorkerManager workers;

        readonly List<NeedStateTracker> m_Trackers = new List<NeedStateTracker>();

        public IReadOnlyList<NeedStateTracker> Trackers => m_Trackers;

        public event Action<Need, NeedState, NeedState> StateChanged;

        public NeedState StateOf(Need need)
        {
            foreach (var tracker in m_Trackers)
            {
                if (tracker.Need == need) return tracker.State;
            }
            return NeedState.Ok;
        }

        void Awake()
        {
            var settings = WaveConfig.Current.NeedStates;
            foreach (Need need in Enum.GetValues(typeof(Need)))
            {
                var tracker = new NeedStateTracker(need, settings.BottleneckThreshold, settings.BottleneckHysteresisSeconds);
                tracker.StateChanged += OnTrackerChanged;
                m_Trackers.Add(tracker);
            }
        }

        void Start()
        {
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged += OnWaveStateChanged;
        }

        void OnDestroy()
        {
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged -= OnWaveStateChanged;
            foreach (var tracker in m_Trackers)
                tracker.StateChanged -= OnTrackerChanged;
        }

        void Update()
        {
            if (waveLoop == null || workers == null || waveLoop.IsPaused) return;

            var release = waveLoop.Release;
            foreach (var tracker in m_Trackers)
            {
                var backlog = 0;
                var pending = 0;
                foreach (var building in workers.Buildings)
                {
                    if (building == null || building.Need != tracker.Need || building.Queue == null) continue;
                    backlog += building.Queue.BacklogCount;
                    pending += building.Queue.PendingCount;
                }

                var demand = release.Demand(tracker.Need);
                var allServed = demand > 0 && release.Released(tracker.Need) >= demand && pending == 0;
                tracker.Update(waveLoop.WaveTime, backlog, allServed);
            }
        }

        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            if (next != WaveState.WaveIncoming) return;
            foreach (var tracker in m_Trackers)
                tracker.Reset();
        }

        void OnTrackerChanged(Need need, NeedState previous, NeedState next)
        {
            Debug.Log($"Wave {waveLoop.Wave}: {need} {previous.ToJiraName()} → {next.ToJiraName()}");
            StateChanged?.Invoke(need, previous, next);
        }

#if UNITY_EDITOR
        readonly StringBuilder m_Label = new StringBuilder();

        void OnGUI()
        {
            if (waveLoop == null) return;

            m_Label.Clear();
            foreach (var tracker in m_Trackers)
            {
                if (waveLoop.Release.Demand(tracker.Need) == 0) continue;
                if (m_Label.Length > 0) m_Label.Append("   ");
                m_Label.Append($"{tracker.Need}: {tracker.State.ToJiraName()}");
            }
            GUI.Label(new Rect(10, 34, 900, 24), m_Label.ToString());
        }
#endif
    }
}
