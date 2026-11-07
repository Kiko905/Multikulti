using System;
using System.Collections.Generic;

namespace Multikulti.Buildings
{
    public sealed class BuildingQueue
    {
        readonly List<Unit> m_Seated = new List<Unit>();
        readonly Queue<Unit> m_Backlog = new Queue<Unit>();

        public Need Need { get; }
        public int Capacity { get; private set; }
        public IReadOnlyList<Unit> Seated => m_Seated;
        public int BacklogCount => m_Backlog.Count;
        public int FreeSeats => Capacity - m_Seated.Count;

        public event Action<Unit> UnitSeated;
        public event Action<Unit> UnitQueued;
        public event Action<Unit> UnitServed;

        public BuildingQueue(Need need, int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Need = need;
            Capacity = capacity;
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

        public void CompleteService(Unit unit, float now)
        {
            if (!m_Seated.Remove(unit))
                throw new InvalidOperationException("Unit is not seated in this building.");
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
            m_Seated.Add(unit);
            UnitSeated?.Invoke(unit);
        }
    }
}
