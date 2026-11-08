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

        public WorkerRoster Roster { get; private set; }
        public IReadOnlyList<Building> Buildings => buildings;

        void Awake()
        {
            Roster = new WorkerRoster(buildings.Count);
            Roster.WorkerMoved += OnWorkerMoved;
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
        }

        void Start() => Refresh();

        void OnDestroy()
        {
            if (Roster != null)
                Roster.WorkerMoved -= OnWorkerMoved;
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

        void OnWorkerMoved(int worker, int from, int to) => Refresh();

        void Refresh()
        {
            for (var i = 0; i < buildings.Count; i++)
                buildings[i].HasWorker = Roster.HasWorker(i);
        }
    }
}
