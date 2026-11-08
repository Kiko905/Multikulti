namespace Multikulti.Buildings
{
    public sealed class Unit
    {
        public Need Need { get; }
        public float ArrivedAt { get; }
        public float? SeatedAt { get; internal set; }
        public float ServiceProgress { get; internal set; }

        public Unit(Need need, float arrivedAt)
        {
            Need = need;
            ArrivedAt = arrivedAt;
        }
    }
}
