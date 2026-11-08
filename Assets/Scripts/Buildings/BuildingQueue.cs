using System;
using System.Collections.Generic;

namespace Multikulti.Buildings
{
    public sealed class BuildingQueue
    {
        readonly List<Unit> m_Seated = new List<Unit>();
        readonly Queue<Unit> m_Backlog = new Queue<Unit>();
        readonly List<Unit> m_Finished = new List<Unit>();

        float m_Utilization = 1f;
        float m_SpeedMultiplier = 1f;

        public Need Need { get; }
        public int Capacity { get; private set; }
        public float ServiceTime { get; }
        public IReadOnlyList<Unit> Seated => m_Seated;
        public int BacklogCount => m_Backlog.Count;
        public int FreeSeats => Capacity - m_Seated.Count;

        public float Utilization
        {
            get => m_Utilization;
            set
            {
                if (value <= 0f) throw new ArgumentOutOfRangeException(nameof(value));
                m_Utilization = value;
            }
        }

        public float SpeedMultiplier
        {
            get => m_SpeedMultiplier;
            set
            {
                if (value <= 0f) throw new ArgumentOutOfRangeException(nameof(value));
                m_SpeedMultiplier = value;
            }
        }

        public event Action<Unit> UnitSeated;
        public event Action<Unit> UnitQueued;
        public event Action<Unit> UnitServed;

        public BuildingQueue(Need need, int capacity, float serviceTime)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (serviceTime <= 0f) throw new ArgumentOutOfRangeException(nameof(serviceTime));
            Need = need;
            Capacity = capacity;
            ServiceTime = serviceTime;
        }

        public void Enqueue(Unit unit, float now)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (unit.Need != Need)
                throw new ArgumentException($"Building serves {Need}, got {unit.Need}.");

            if (FreeSeats > 0)
            {
                Seat(unit, now);
            }
            else
            {
                m_Backlog.Enqueue(unit);
                UnitQueued?.Invoke(unit);
            }
        }

        public void Tick(float deltaTime, float now)
        {
            if (deltaTime <= 0f || m_Seated.Count == 0) return;

            var step = deltaTime * Utilization * SpeedMultiplier / ServiceTime;
            foreach (var unit in m_Seated)
            {
                unit.ServiceProgress = Math.Min(1f, unit.ServiceProgress + step);
                if (unit.ServiceProgress >= 1f) m_Finished.Add(unit);
            }

            foreach (var unit in m_Finished)
                CompleteService(unit, now);
            m_Finished.Clear();
        }

        public void CompleteService(Unit unit, float now)
        {
            if (!m_Seated.Remove(unit))
                throw new InvalidOperationException("Unit is not seated in this building.");
            unit.ServiceProgress = 1f;
            UnitServed?.Invoke(unit);
            FillSeats(now);
        }

        public void SetCapacity(int capacity, float now)
        {
            if (capacity < Capacity) throw new ArgumentException("Capacity can only grow.");
            Capacity = capacity;
            FillSeats(now);
        }

        void FillSeats(float now)
        {
            while (FreeSeats > 0 && m_Backlog.Count > 0)
                Seat(m_Backlog.Dequeue(), now);
        }

        void Seat(Unit unit, float now)
        {
            unit.SeatedAt = now;
            unit.ServiceProgress = 0f;
            m_Seated.Add(unit);
            UnitSeated?.Invoke(unit);
        }
    }
}
