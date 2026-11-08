using System.Collections.Generic;
using Multikulti.Buildings;
using UnityEngine;

namespace Multikulti.Workers
{
    public sealed class WorkerManager : MonoBehaviour
    {
        [SerializeField] List<Building> buildings = new List<Building>();

        public WorkerRoster Roster { get; private set; }
        public IReadOnlyList<Building> Buildings => buildings;

        void Awake()
        {
            Roster = new WorkerRoster(buildings.Count);
            Roster.WorkerMoved += OnWorkerMoved;
            Refresh();
        }

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

        void OnWorkerMoved(int worker, int from, int to) => Refresh();

        void Refresh()
        {
            for (var i = 0; i < buildings.Count; i++)
                buildings[i].HasWorker = Roster.HasWorker(i);
        }
    }
}
