using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Multikulti.Config
{
    /// <summary>
    /// Tunable parameters of the wave system (SCRUM-90).
    /// The values live in Assets/Resources/Config/wave_config.json, not in code.
    /// A wave_config.json in Application.persistentDataPath overrides them on a device without a new build.
    /// </summary>
    [Serializable]
    public sealed class WaveConfig
    {
        public const string ResourcePath = "Config/wave_config";
        public const string OverrideFileName = "wave_config.json";
        public const int SupportedSchemaVersion = 1;
        public const int RatingCount = 6;

        static WaveConfig s_Current;

        [SerializeField] int schema_version;
        [SerializeField] WaveSettings wave = new WaveSettings();
        [SerializeField] ServiceSettings service = new ServiceSettings();
        [SerializeField] NeedStateSettings need_states = new NeedStateSettings();
        [SerializeField] SatisfactionSettings satisfaction = new SatisfactionSettings();
        [SerializeField] RewardSettings rewards = new RewardSettings();
        [SerializeField] BuildingSettings buildings = new BuildingSettings();
        [SerializeField] UnlockSettings unlocks = new UnlockSettings();
        [SerializeField] SpeedUpSettings speed_up = new SpeedUpSettings();
        [SerializeField] SuddenRushSettings sudden_rush = new SuddenRushSettings();
        [SerializeField] WaveDefinition[] waves = Array.Empty<WaveDefinition>();

        public int SchemaVersion => schema_version;
        public WaveSettings Wave => wave;
        public ServiceSettings Service => service;
        public NeedStateSettings NeedStates => need_states;
        public SatisfactionSettings Satisfaction => satisfaction;
        public RewardSettings Rewards => rewards;
        public BuildingSettings Buildings => buildings;
        public UnlockSettings Unlocks => unlocks;
        public SpeedUpSettings SpeedUp => speed_up;
        public SuddenRushSettings SuddenRush => sudden_rush;
        /// <summary>Scripted waves, wave 1 first (build document §4.8).</summary>
        public IReadOnlyList<WaveDefinition> Waves => waves;

        /// <summary>The loaded configuration; loads it on first use.</summary>
        public static WaveConfig Current => s_Current ??= Load();

        /// <summary>Reads the configuration again, e.g. after editing the override file during a playtest.</summary>
        public static WaveConfig Reload()
        {
            s_Current = Load();
            return s_Current;
        }

        // Keeps Current correct when Enter Play Mode options skip the domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCurrent() => s_Current = null;

        /// <summary>Loads the bundled configuration and applies the persistentDataPath override if it exists.</summary>
        public static WaveConfig Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw new InvalidOperationException($"Wave config not found at Resources/{ResourcePath}.json.");

            var config = FromJson(asset.text);

            var overridePath = Path.Combine(Application.persistentDataPath, OverrideFileName);
            if (File.Exists(overridePath))
            {
                // Overwrite keeps every value the override file does not mention.
                JsonUtility.FromJsonOverwrite(File.ReadAllText(overridePath), config);
                Debug.Log($"Wave config: using override {overridePath}");
            }

            foreach (var problem in config.Validate())
                Debug.LogWarning($"Wave config: {problem}");

            return config;
        }

        public static WaveConfig FromJson(string json)
        {
            var config = JsonUtility.FromJson<WaveConfig>(json);
            if (config == null)
                throw new FormatException("Wave config JSON is empty or invalid.");
            return config;
        }

        /// <summary>Lists invalid values and placeholders (0) that still wait for balance numbers.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (schema_version != SupportedSchemaVersion)
                problems.Add($"schema_version is {schema_version}, expected {SupportedSchemaVersion}.");

            RequirePositive(problems, "wave.wave_timer", wave.WaveTimer);
            RequirePositive(problems, "service.service_time", service.ServiceTime);
            RequirePositive(problems, "service.utilization_worker", service.UtilizationWorker);
            // SCRUM-86: a building without a Worker must never stop, otherwise a wave could never end.
            RequirePositive(problems, "service.utilization_empty", service.UtilizationEmpty);
            if (service.UtilizationEmpty > service.UtilizationWorker)
                problems.Add("service.utilization_empty is higher than service.utilization_worker.");

            RequirePositive(problems, "need_states.bottleneck_threshold", need_states.BottleneckThreshold);
            RequirePositive(problems, "need_states.bottleneck_hysteresis_s", need_states.BottleneckHysteresisSeconds);

            // SCRUM-96 (WR1): unit_floor < wave_floor < 3★ threshold.
            if (satisfaction.UnitFloor >= satisfaction.WaveFloor)
                problems.Add("satisfaction.unit_floor must be lower than satisfaction.wave_floor.");
            RequireCount(problems, "satisfaction.rating_thresholds", satisfaction.RatingThresholds, RatingCount);
            RequireAscending(problems, "satisfaction.rating_thresholds", satisfaction.RatingThresholds);
            if (satisfaction.RatingThresholds.Count >= 3 && satisfaction.WaveFloor >= satisfaction.RatingThresholds[2])
                problems.Add("satisfaction.wave_floor must be lower than the 3★ threshold (rating_thresholds[2]).");
            RequirePositive(problems, "satisfaction.patience_grace_s", satisfaction.PatienceGraceSeconds);
            if (satisfaction.PatienceFloorAfterSeconds <= satisfaction.PatienceGraceSeconds)
                problems.Add("satisfaction.patience_floor_after_s must be greater than satisfaction.patience_grace_s.");
            RequirePositive(problems, "satisfaction.ocean_view_weight", satisfaction.OceanViewWeight);

            RequirePositive(problems, "rewards.coins_per_unit", rewards.CoinsPerUnit);
            RequireCount(problems, "rewards.rating_coin_multipliers", rewards.RatingCoinMultipliers, RatingCount);
            RequireNoPlaceholders(problems, "rewards.rating_coin_multipliers", rewards.RatingCoinMultipliers);
            RequireCount(problems, "rewards.energy_per_rating", rewards.EnergyPerRating, RatingCount);

            RequireAscending(problems, "buildings.capacity_by_level", buildings.CapacityByLevel);
            RequireCount(problems, "buildings.upgrade_prices", buildings.UpgradePrices, buildings.CapacityByLevel.Count - 1);
            RequireNoPlaceholders(problems, "buildings.upgrade_prices", buildings.UpgradePrices);

            RequirePositive(problems, "unlocks.pool_price", unlocks.PoolPrice);
            RequirePositive(problems, "unlocks.bungalows_price", unlocks.BungalowsPrice);

            if (waves.Length == 0)
                problems.Add("waves is empty.");
            for (var i = 0; i < waves.Length; i++)
            {
                if (waves[i].Wave != i + 1)
                    problems.Add($"waves[{i}].wave is {waves[i].Wave}, expected {i + 1}.");
            }

            return problems;
        }

        static void RequirePositive(List<string> problems, string name, float value)
        {
            if (value == 0f)
                problems.Add($"{name} is 0 (placeholder, waiting for a balance value).");
            else if (value < 0f)
                problems.Add($"{name} must be greater than 0.");
        }

        static void RequireCount<T>(List<string> problems, string name, IReadOnlyList<T> values, int expected)
        {
            if (values.Count != expected)
                problems.Add($"{name} has {values.Count} values, expected {expected}.");
        }

        static void RequireAscending(List<string> problems, string name, IReadOnlyList<float> values)
        {
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] <= values[i - 1])
                {
                    problems.Add($"{name} must be in ascending order.");
                    return;
                }
            }
        }

        static void RequireAscending(List<string> problems, string name, IReadOnlyList<int> values)
        {
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] <= values[i - 1])
                {
                    problems.Add($"{name} must be in ascending order.");
                    return;
                }
            }
        }

        static void RequireNoPlaceholders(List<string> problems, string name, IReadOnlyList<float> values)
        {
            foreach (var value in values)
            {
                if (value == 0f)
                {
                    problems.Add($"{name} contains 0 (placeholder, waiting for balance values).");
                    return;
                }
            }
        }

        static void RequireNoPlaceholders(List<string> problems, string name, IReadOnlyList<int> values)
        {
            foreach (var value in values)
            {
                if (value == 0)
                {
                    problems.Add($"{name} contains 0 (placeholder, waiting for balance values).");
                    return;
                }
            }
        }
    }

    /// <summary>Arrival phase of a wave (SCRUM-24, SCRUM-25).</summary>
    [Serializable]
    public sealed class WaveSettings
    {
        [SerializeField] float wave_timer;

        /// <summary>Length of the arrival phase in seconds; demand is released at demand_total ÷ wave_timer.</summary>
        public float WaveTimer => wave_timer;
    }

    /// <summary>Service speed of buildings (SCRUM-31, SCRUM-38, SCRUM-86).</summary>
    [Serializable]
    public sealed class ServiceSettings
    {
        [SerializeField] float service_time;
        [SerializeField] float utilization_worker;
        [SerializeField] float utilization_empty;

        /// <summary>Seconds to serve one unit at utilization 1.0; actual time is service_time ÷ utilization.</summary>
        public float ServiceTime => service_time;
        /// <summary>Utilization of a building with a Worker.</summary>
        public float UtilizationWorker => utilization_worker;
        /// <summary>Utilization of a building without a Worker; must stay above 0.</summary>
        public float UtilizationEmpty => utilization_empty;
    }

    /// <summary>Switching between OK, BOTTLENECK, RECOVERING and SATISFIED (SCRUM-32).</summary>
    [Serializable]
    public sealed class NeedStateSettings
    {
        [SerializeField] float bottleneck_threshold;
        [SerializeField] float bottleneck_hysteresis_s;

        /// <summary>Queue length at which a need becomes BOTTLENECK.</summary>
        public float BottleneckThreshold => bottleneck_threshold;
        /// <summary>Seconds a state must hold before the need switches, to avoid flickering.</summary>
        public float BottleneckHysteresisSeconds => bottleneck_hysteresis_s;
    }

    /// <summary>Wave Satisfaction and rating (SCRUM-43, SCRUM-44).</summary>
    [Serializable]
    public sealed class SatisfactionSettings
    {
        [SerializeField] float unit_floor;
        [SerializeField] float wave_floor;
        [SerializeField] float[] rating_thresholds = Array.Empty<float>();
        [SerializeField] float patience_grace_s;
        [SerializeField] float patience_floor_after_s;
        [SerializeField] float ocean_view_weight;

        /// <summary>Lowest contribution of one unit, as a fraction.</summary>
        public float UnitFloor => unit_floor;
        /// <summary>Lowest Wave Satisfaction, as a fraction.</summary>
        public float WaveFloor => wave_floor;
        /// <summary>Satisfaction thresholds for the ratings, lowest first, as fractions.</summary>
        public IReadOnlyList<float> RatingThresholds => rating_thresholds;
        /// <summary>Waiting up to this many seconds (arrival → seated) contributes 100 %.</summary>
        public float PatienceGraceSeconds => patience_grace_s;
        /// <summary>From this many seconds of waiting a unit contributes only unit_floor; linear in between.</summary>
        public float PatienceFloorAfterSeconds => patience_floor_after_s;
        /// <summary>Satisfaction weight of an Ocean View unit; volume needs weigh 1.0.</summary>
        public float OceanViewWeight => ocean_view_weight;
    }

    /// <summary>Coins and Energy after a wave (SCRUM-45).</summary>
    [Serializable]
    public sealed class RewardSettings
    {
        [SerializeField] float coins_per_unit;
        [SerializeField] float[] rating_coin_multipliers = Array.Empty<float>();
        [SerializeField] int[] energy_per_rating = Array.Empty<int>();

        /// <summary>Coins = wave units × coins_per_unit × rating multiplier.</summary>
        public float CoinsPerUnit => coins_per_unit;
        /// <summary>Coin multiplier per rating, same order as the rating thresholds.</summary>
        public IReadOnlyList<float> RatingCoinMultipliers => rating_coin_multipliers;
        /// <summary>Energy granted per rating, same order as the rating thresholds.</summary>
        public IReadOnlyList<int> EnergyPerRating => energy_per_rating;
    }

    /// <summary>Building levels and upgrade prices (SCRUM-50).</summary>
    [Serializable]
    public sealed class BuildingSettings
    {
        [SerializeField] int[] capacity_by_level = Array.Empty<int>();
        [SerializeField] int[] upgrade_prices = Array.Empty<int>();

        /// <summary>Capacity (seats) per building level, level 1 first.</summary>
        public IReadOnlyList<int> CapacityByLevel => capacity_by_level;
        /// <summary>Coins price of each upgrade; index 0 is level 1 → 2.</summary>
        public IReadOnlyList<int> UpgradePrices => upgrade_prices;
    }

    /// <summary>Area Unlocks and debut waves (SCRUM-51, SCRUM-54).</summary>
    [Serializable]
    public sealed class UnlockSettings
    {
        [SerializeField] int pool_after_wave;
        [SerializeField] int pool_price;
        [SerializeField] int bungalows_after_wave;
        [SerializeField] int bungalows_price;
        [SerializeField] float debut_need_volume_ratio;

        public int PoolAfterWave => pool_after_wave;
        public int PoolPrice => pool_price;
        public int BungalowsAfterWave => bungalows_after_wave;
        public int BungalowsPrice => bungalows_price;
        /// <summary>Volume of a new need in its debut wave relative to an established need.</summary>
        public float DebutNeedVolumeRatio => debut_need_volume_ratio;
    }

    /// <summary>Energy boost Speed Up (SCRUM-57).</summary>
    [Serializable]
    public sealed class SpeedUpSettings
    {
        [SerializeField] int energy_cost;
        [SerializeField] float duration_s;
        [SerializeField] float service_time_multiplier;
        [SerializeField] int max_per_building_per_wave;

        public int EnergyCost => energy_cost;
        public float DurationSeconds => duration_s;
        /// <summary>Factor applied to service_time while the boost runs (0.5 = half the time).</summary>
        public float ServiceTimeMultiplier => service_time_multiplier;
        public int MaxPerBuildingPerWave => max_per_building_per_wave;
    }

    /// <summary>Mini-event Sudden rush (SCRUM-60).</summary>
    [Serializable]
    public sealed class SuddenRushSettings
    {
        [SerializeField] int wave;
        [SerializeField] float time_s;
        [SerializeField] int extra_units;
        [SerializeField] float duration_s;

        /// <summary>Wave number in which the event starts.</summary>
        public int Wave => wave;
        /// <summary>Seconds into the arrival phase when the rush starts.</summary>
        public float TimeSeconds => time_s;
        public int ExtraUnits => extra_units;
        public float DurationSeconds => duration_s;
    }

    /// <summary>One scripted wave: demand_total per need, 0 = the need is not in this wave (build document §4.8).</summary>
    [Serializable]
    public sealed class WaveDefinition
    {
        [SerializeField] int wave;
        [SerializeField] int rooms;
        [SerializeField] int drinks;
        [SerializeField] int food;
        [SerializeField] int relax;
        [SerializeField] int ocean_view;

        public int Wave => wave;
        public int Rooms => rooms;
        public int Drinks => drinks;
        public int Food => food;
        public int Relax => relax;
        public int OceanView => ocean_view;
    }
}
