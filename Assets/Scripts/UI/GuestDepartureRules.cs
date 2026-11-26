using UnityEngine;

namespace Multikulti.UI
{
    /// <summary>
    /// How many guest figures leave after a wave (SCRUM-47, build document §4.4 "Hostia počas vlny NEODCHÁDZAJÚ"):
    /// nobody leaves during the wave; on Wave Complete a part of the guests leaves because they ran out of patience.
    ///
    /// The number comes from the whole Wave Satisfaction, not from what happened to single guests (the game does
    /// not follow guests): from the 5★ threshold up nobody leaves, at wave_floor (1★) a visible group leaves,
    /// in between it grows evenly (5★ = nobody, 3★ = a few, 1★ = a visible group).
    /// It is only a picture of the result: it has no effect on the next wave or on the rewards.
    /// </summary>
    public static class GuestDepartureRules
    {
        /// <summary>Guest figures on the Wave Complete screen.</summary>
        public const int Figures = 8;

        /// <summary>Most figures that leave (at wave_floor); the rest always stays.</summary>
        public const int MaxLeaving = 6;

        /// <summary>
        /// Figures that leave: 0 from happyFrom (the 5★ threshold) up, MaxLeaving at floor (wave_floor) and below,
        /// rounded up in between so that every wave below 5★ shows at least one.
        /// </summary>
        public static int Leaving(float satisfaction, float happyFrom, float floor)
        {
            if (satisfaction + 0.0001f >= happyFrom) return 0;
            var span = happyFrom - floor;
            if (span <= 0f) return MaxLeaving;
            var share = Mathf.Clamp01((happyFrom - satisfaction) / span);
            return Mathf.Clamp(Mathf.CeilToInt(share * MaxLeaving - 0.0001f), 1, MaxLeaving);
        }
    }
}
