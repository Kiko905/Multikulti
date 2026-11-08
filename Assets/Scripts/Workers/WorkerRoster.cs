using System;
using System.Collections.Generic;

namespace Multikulti.Workers
{
    public sealed class WorkerRoster
    {
        readonly List<int> m_BuildingOfWorker = new List<int>();

        public int BuildingCount { get; private set; }
        public int WorkerCount => m_BuildingOfWorker.Count;

        public event Action<int, int, int> WorkerMoved;

        public WorkerRoster(int buildingCount)
        {
            if (buildingCount < 2)
                throw new ArgumentOutOfRangeException(nameof(buildingCount), "Potrebujeme aspoň 2 budovy.");

            BuildingCount = buildingCount;
            for (var b = 0; b < buildingCount - 1; b++)
                m_BuildingOfWorker.Add(b);

            AssertValid();
        }

        public bool HasWorker(int building) => m_BuildingOfWorker.Contains(building);

        public int WorkerOn(int building) => m_BuildingOfWorker.IndexOf(building);

        public int BuildingOf(int worker) => m_BuildingOfWorker[worker];

        public int EmptyBuilding
        {
            get
            {
                for (var b = 0; b < BuildingCount; b++)
                    if (!HasWorker(b)) return b;
                throw new InvalidOperationException("Žiadna prázdna budova.");
            }
        }

        public int AddBuilding()
        {
            var target = EmptyBuilding;
            var newBuilding = BuildingCount++;
            var worker = m_BuildingOfWorker.Count;
            m_BuildingOfWorker.Add(target);

            AssertValid();
            WorkerMoved?.Invoke(worker, -1, target);
            return newBuilding;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public void AssertValid()
        {
            UnityEngine.Debug.Assert(WorkerCount == BuildingCount - 1,
                $"Workerov je {WorkerCount}, budov {BuildingCount} – má byť o 1 menej.");

            var seen = new HashSet<int>();
            foreach (var b in m_BuildingOfWorker)
            {
                UnityEngine.Debug.Assert(b >= 0 && b < BuildingCount, $"Worker stojí na neexistujúcej budove {b}.");
                UnityEngine.Debug.Assert(seen.Add(b), $"Na budove {b} sú dvaja Workeri.");
            }
        }
    }
}
