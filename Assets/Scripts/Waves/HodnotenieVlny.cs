using System;
using System.Collections.Generic;

namespace Multikulti.Waves
{
    /// <summary>Rating of a finished wave (SCRUM-44): 1★ to 5★ or Perfect, in the order of rating_thresholds.</summary>
    public enum WaveRating
    {
        OneStar = 1,
        TwoStars = 2,
        ThreeStars = 3,
        FourStars = 4,
        FiveStars = 5,
        Perfect = 6
    }

    /// <summary>
    /// Result of a finished wave — the object "hodnotenie : HodnotenieVlny" of the activity diagram (SCRUM-44).
    /// WaveLoop creates it in entry / VypocitatRating of the state "Vlna dokončená" (WaveResolving);
    /// "Zobraziť hodnotenie vlny" (Wave Complete screen) reads it. Coins and Energy come with SCRUM-45.
    /// </summary>
    public sealed class HodnotenieVlny
    {
        /// <summary>Tolerance for comparing Wave Satisfaction with a threshold, so 100 % counts as Perfect.</summary>
        const float ThresholdEpsilon = 0.0001f;

        public HodnotenieVlny(int wave, WaveSatisfactionResult satisfaction, WaveRating rating)
        {
            Wave = wave;
            Satisfaction = satisfaction;
            Rating = rating;
        }

        /// <summary>Number of the rated wave.</summary>
        public int Wave { get; }

        /// <summary>Wave Satisfaction with the per-need breakdown (SCRUM-43, "Vypočítať spokojnosť hostí").</summary>
        public WaveSatisfactionResult Satisfaction { get; }

        /// <summary>1★ … 5★ or Perfect.</summary>
        public WaveRating Rating { get; }

        /// <summary>Number of stars shown, 1–5; Perfect shows 5 stars.</summary>
        public int Stars => Math.Min((int)Rating, 5);

        public bool IsPerfect => Rating == WaveRating.Perfect;

        /// <summary>Rating as written in the build document and playtest metrics: "1★" … "5★" or "Perfect".</summary>
        public string RatingLabel => Label(Rating);

        public static string Label(WaveRating rating) =>
            rating == WaveRating.Perfect ? "Perfect" : $"{(int)rating}★";

        /// <summary>
        /// SCRUM-44: turns Wave Satisfaction (0–1) into a rating. thresholds are the 6 values of rating_thresholds,
        /// lowest first (1★ 40 % · 2★ 55 % · 3★ 70 % · 4★ 80 % · 5★ 90 % · Perfect 100 %): the rating is the
        /// highest threshold the satisfaction reaches. The lowest possible result is 1★, even below the 1★ threshold.
        /// </summary>
        public static WaveRating RatingFor(float satisfaction, IReadOnlyList<float> thresholds)
        {
            if (thresholds == null) throw new ArgumentNullException(nameof(thresholds));
            var rating = WaveRating.OneStar;
            var count = Math.Min(thresholds.Count, (int)WaveRating.Perfect);
            for (var i = 0; i < count; i++)
            {
                if (satisfaction + ThresholdEpsilon >= thresholds[i])
                    rating = (WaveRating)(i + 1);
            }
            return rating;
        }
    }
}
