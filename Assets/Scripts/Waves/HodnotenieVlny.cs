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
    /// "Zobraziť hodnotenie vlny" (Wave Complete screen) reads it.
    ///
    /// Reward (SCRUM-45, build document §4.8 "Príjem z vlny"): Coins = units of the wave × coins_per_unit ×
    /// rating multiplier, Energy by the rating (energy_per_rating). It is calculated together with the rating and
    /// credited only when the player takes it ("Zobrať odmenu", WaveLoop.ZobratOdmenu).
    /// </summary>
    public sealed class HodnotenieVlny
    {
        /// <summary>Tolerance for comparing Wave Satisfaction with a threshold, so 100 % counts as Perfect.</summary>
        const float ThresholdEpsilon = 0.0001f;

        public HodnotenieVlny(int wave, WaveSatisfactionResult satisfaction, WaveRating rating,
            int units = 0, int coins = 0, int energy = 0)
        {
            if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
            if (coins < 0) throw new ArgumentOutOfRangeException(nameof(coins));
            if (energy < 0) throw new ArgumentOutOfRangeException(nameof(energy));
            Wave = wave;
            Satisfaction = satisfaction;
            Rating = rating;
            Units = units;
            Coins = coins;
            Energy = energy;
        }

        /// <summary>Number of the rated wave.</summary>
        public int Wave { get; }

        /// <summary>Wave Satisfaction with the per-need breakdown (SCRUM-43, "Vypočítať spokojnosť hostí").</summary>
        public WaveSatisfactionResult Satisfaction { get; }

        /// <summary>1★ … 5★ or Perfect.</summary>
        public WaveRating Rating { get; }

        /// <summary>
        /// Units of the wave the reward is counted from: the whole demand of the wave including Sudden rush units.
        /// Every unit is always served, so this is the whole volume of the wave (build document §4.8).
        /// </summary>
        public int Units { get; }

        /// <summary>Coins for the wave (SCRUM-45): Units × coins_per_unit × rating multiplier, rounded.</summary>
        public int Coins { get; }

        /// <summary>Energy for the wave (SCRUM-45): energy_per_rating of the rating.</summary>
        public int Energy { get; }

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
        /// <summary>
        /// SCRUM-45: Coins = units × coinsPerUnit × multiplier of the rating, rounded half away from zero
        /// (e.g. 22 × 16 × 0.8 = 281.6 → 282). multipliers are the 6 values of rating_coin_multipliers,
        /// 1★ first and Perfect last. The player cannot earn more by playing longer or worse: only the rating changes
        /// the multiplier.
        /// </summary>
        public static int CoinsFor(int units, float coinsPerUnit, IReadOnlyList<float> multipliers, WaveRating rating)
        {
            if (multipliers == null) throw new ArgumentNullException(nameof(multipliers));
            if (units <= 0 || coinsPerUnit <= 0f) return 0;
            var multiplier = ValueFor(multipliers, rating);
            if (multiplier <= 0f) return 0;
            return (int)Math.Round((double)units * coinsPerUnit * multiplier, MidpointRounding.AwayFromZero);
        }

        /// <summary>SCRUM-45: Energy of the rating from energy_per_rating (6 values, 1★ first, Perfect last).</summary>
        public static int EnergyFor(IReadOnlyList<int> energyPerRating, WaveRating rating)
        {
            if (energyPerRating == null) throw new ArgumentNullException(nameof(energyPerRating));
            return Math.Max(0, ValueFor(energyPerRating, rating));
        }

        /// <summary>Value of a per-rating list; a missing value counts as 0.</summary>
        static T ValueFor<T>(IReadOnlyList<T> values, WaveRating rating)
        {
            var index = (int)rating - 1;
            return index >= 0 && index < values.Count ? values[index] : default;
        }

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
