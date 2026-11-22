using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.Workers
{
    public sealed class WorkerManager : MonoBehaviour
    {
        [SerializeField] List<Building> buildings = new List<Building>();
        [SerializeField] WaveLoop waveLoop;

        int[] m_Layout;

        public WorkerRoster Roster { get; private set; }
        public IReadOnlyList<Building> Buildings => buildings;

        void Awake()
        {
            Roster = new WorkerRoster(buildings.Count);
            Roster.WorkerMoved += OnWorkerMoved;
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
        }

        void Start()
        {
            m_Layout = Layout();
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged += OnWaveStateChanged;
            Refresh();
        }

        void OnDestroy()
        {
            if (Roster != null)
                Roster.WorkerMoved -= OnWorkerMoved;
            if (waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged -= OnWaveStateChanged;
        }

        public void AddBuilding(Building building)
        {
            buildings.Add(building);
            Roster.AddBuilding();
        }

        public WaveState WaveState => waveLoop != null && waveLoop.Machine != null ? waveLoop.State : WaveState.AwaitingWave;

        public bool CanMove() => IsMoveAllowed(WaveState);

        public static bool IsMoveAllowed(WaveState state) =>
            state == WaveState.AwaitingWave ||
            state == WaveState.WaveIncoming ||
            state == WaveState.WaveActive;

        public bool TryMoveFrom(Building building)
        {
            if (!CanMove()) return false;

            var from = buildings.IndexOf(building);
            if (from < 0) return false;

            var worker = Roster.WorkerOn(from);
            if (worker < 0) return false;

            var target = Roster.ChooseTarget(from);
            if (target < 0) return false;

            return Roster.Move(worker, target);
        }

        void OnWorkerMoved(int worker, int from, int to)
        {
            m_Layout = Layout();
            Refresh();
        }

        void Refresh()
        {
            for (var i = 0; i < buildings.Count; i++)
                buildings[i].HasWorker = Roster.HasWorker(i);
        }

        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            if (next != WaveState.WaveIncoming) return;

            var current = Layout();
            Debug.Assert(SameLayout(current, m_Layout),
                $"Wave {waveLoop.Wave}: Workers changed between waves without a move: [{string.Join(", ", m_Layout)}] → [{string.Join(", ", current)}]");
            Debug.Log($"Wave {waveLoop.Wave} starts with Workers on [{string.Join(", ", current)}]");
        }

        int[] Layout()
        {
            var layout = new int[Roster.WorkerCount];
            for (var w = 0; w < layout.Length; w++)
                layout[w] = Roster.BuildingOf(w);
            return layout;
        }

        static bool SameLayout(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
