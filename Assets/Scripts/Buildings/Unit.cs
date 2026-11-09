namespace Multikulti.Buildings
{
    /// <summary>One unit of a need: one guest with that need (Docs/Slovnik.md).</summary>
    public sealed class Unit
    {
        public Need Need { get; }
        public float ArrivedAt { get; }
        public float? SeatedAt { get; internal set; }
        public float ServiceProgress { get; internal set; }

        /// <summary>
        /// Waiting of the unit (SCRUM-42): seconds from arrival until it was seated.
        /// The service itself does not count. Null while the unit still waits in the backlog.
        /// </summary>
        public float? WaitingSeconds => SeatedAt.HasValue ? System.Math.Max(0f, SeatedAt.Value - ArrivedAt) : (float?)null;

        public Unit(Need need, float arrivedAt)
        {
            Need = need;
            ArrivedAt = arrivedAt;
        }
    }
}
