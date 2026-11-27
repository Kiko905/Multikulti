using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Unlocks;
using Multikulti.Waves;
using Multikulti.Workers;
using UnityEngine;

namespace Multikulti.Economy
{
    /// <summary>
    /// Keeps ResortBalances.ResortValue in step with the resort (SCRUM-56, build document §5.5): Resort Value is the
    /// status of the resort — the value of its buildings plus what its finished waves earned (ResortValueRules).
    ///
    /// - On start the buildings already standing give the start value (3 × building_base).
    /// - An upgrade (UpgradeService.Upgraded) and a bought area (AreaUnlockService.Bought) raise it by what they added.
    /// - Every rated wave (WaveLoop.WaveRated, in WaveResolving) adds per_rating of its rating, before the
    ///   Wave Complete screen opens, so S4 shows it as "+N Resort Value".
    /// Resort Value never goes down: the service only ever raises it.
    ///
    /// Installs itself at runtime on the ResortBalances, so the scene does not change.
    /// </summary>
    public sealed class ResortValueService : MonoBehaviour
    {
        [SerializeField] ResortBalances balances;
        [SerializeField] WaveLoop waveLoop;
        [SerializeField] WorkerManager workers;
        [SerializeField] UpgradeService upgrades;
        [SerializeField] AreaUnlockService unlocks;

        int m_WavesValue;
        int m_StartValue;
        bool m_Subscribed;

        /// <summary>Resort Value of every building of the resort at its current level.</summary>
        public int BuildingsValue
        {
            get
            {
                if (workers == null) return 0;
                var config = WaveConfig.Current;
                var value = 0;
                foreach (var building in workers.Buildings)
                {
                    if (building != null)
                        value += ResortValueRules.BuildingValue(building.Need, building.Level, config);
                }
                return value;
            }
        }

        /// <summary>Resort Value earned by the finished waves of this session.</summary>
        public int WavesValue => m_WavesValue;

        /// <summary>What Resort Value should be: buildings plus finished waves.</summary>
        public int Expected => BuildingsValue + m_WavesValue;

        /// <summary>Resort Value the next upgrade of the building would add (S5 "Resort Value gain"); 0 at max level.</summary>
        public int UpgradeGain(Building building) =>
            building == null || building.IsMaxLevel ? 0 : ResortValueRules.UpgradeGain(building.Level, WaveConfig.Current);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var balances = FindAnyObjectByType<ResortBalances>();
            if (balances != null && balances.GetComponent<ResortValueService>() == null)
                balances.gameObject.AddComponent<ResortValueService>();
        }

        void Awake()
        {
            if (balances == null)
                balances = GetComponent<ResortBalances>();
            if (balances == null)
                balances = FindAnyObjectByType<ResortBalances>();
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            if (workers == null)
                workers = FindAnyObjectByType<WorkerManager>();
            if (upgrades == null)
                upgrades = FindAnyObjectByType<UpgradeService>();
            if (unlocks == null)
                unlocks = FindAnyObjectByType<AreaUnlockService>();
        }

        // Start: WorkerManager fills its buildings in Awake.
        void Start()
        {
            if (balances == null)
            {
                Debug.LogWarning("Resort Value: no ResortBalances in the scene, Resort Value is not counted.", this);
                enabled = false;
                return;
            }

            if (waveLoop != null)
                waveLoop.WaveRated += OnWaveRated;
            if (upgrades != null)
                upgrades.Upgraded += OnUpgraded;
            if (unlocks != null)
                unlocks.Bought += OnAreaBought;
            m_Subscribed = true;

            // A start value set in the inspector (e.g. for a playtest) stays and the resort adds to it.
            m_StartValue = balances.ResortValue;
            Raise("buildings of the resort");
        }

        void OnDestroy()
        {
            if (!m_Subscribed) return;
            if (waveLoop != null)
                waveLoop.WaveRated -= OnWaveRated;
            if (upgrades != null)
                upgrades.Upgraded -= OnUpgraded;
            if (unlocks != null)
                unlocks.Bought -= OnAreaBought;
        }

        void OnWaveRated(HodnotenieVlny result)
        {
            m_WavesValue += ResortValueRules.RatingValue(result.Rating, WaveConfig.Current);
            Raise($"wave {result.Wave} {result.RatingLabel}");
        }

        void OnUpgraded(Building building) => Raise($"upgrade {building.Need} to L{building.Level}");

        void OnAreaBought(AreaUnlock unlock) => Raise($"area {unlock.Area}");

        /// <summary>Raises Resort Value to the inspector start value + Expected; never lowers it.</summary>
        void Raise(string reason)
        {
            var target = m_StartValue + Expected;
            var current = balances.ResortValue;
            if (target <= current) return;
            balances.ResortValue = target;
            Debug.Log($"Resort Value +{target - current} ({reason}): {current} → {target}", this);
        }
    }
}
