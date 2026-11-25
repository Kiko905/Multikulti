using System;
using System.Collections.Generic;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.Economy
{
    /// <summary>
    /// Energy boost Speed Up (SCRUM-57): at an overloaded building the game offers a boost for energy_cost Energy
    /// (default 3) that halves the building's service_time for duration_s (default 15 s); at most one boost per
    /// building per wave. Values come from speed_up in wave_config.json, the rules from SpeedUpRules.
    ///
    /// Overloaded means the need of the building is BOTTLENECK (SCRUM-32), or the building has the Sudden rush of
    /// this wave (SCRUM-60), whose prompt offers the boost too.
    ///
    /// The boost runs on WaveLoop.WaveTime, so it stops with the wave in WavePaused. When the wave stops running,
    /// running boosts end; a new wave (WaveIncoming) forgets the uses.
    ///
    /// Added to the WaveLoop object at runtime, so the scene does not change.
    /// </summary>
    public sealed class SpeedUpService : MonoBehaviour
    {
        WaveLoop m_Loop;
        NeedStateBoard m_Board;
        ResortBalances m_Balances;
        SpeedUpRules m_Rules;
        bool m_Subscribed;
        readonly List<Building> m_Offers = new List<Building>();

        /// <summary>Raised after a boost started on a building.</summary>
        public event Action<Building> Started;
        /// <summary>Raised after a boost on a building ended.</summary>
        public event Action<Building> Ended;

        public SpeedUpRules Rules => m_Rules;
        public int Energy => m_Balances != null ? m_Balances.Energy : 0;
        bool IsWaveActive => m_Loop != null && m_Loop.Machine != null && m_Loop.State == WaveState.WaveActive;
        float Now => m_Loop != null ? m_Loop.WaveTime : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var loop = FindAnyObjectByType<WaveLoop>();
            if (loop != null && loop.GetComponent<SpeedUpService>() == null)
                loop.gameObject.AddComponent<SpeedUpService>();
        }

        void Awake()
        {
            m_Loop = GetComponent<WaveLoop>();
            m_Board = FindAnyObjectByType<NeedStateBoard>();
            m_Balances = FindAnyObjectByType<ResortBalances>();

            var settings = WaveConfig.Current.SpeedUp;
            m_Rules = new SpeedUpRules(settings.EnergyCost, settings.DurationSeconds,
                settings.ServiceTimeMultiplier, settings.MaxPerBuildingPerWave);
            m_Rules.Ended += OnRuleEnded;

            if (m_Balances == null)
                Debug.LogWarning("Speed Up: no ResortBalances in the scene, Energy is 0 and the boost cannot be bought.", this);
        }

        void Start()
        {
            if (m_Loop == null || m_Loop.Machine == null) return;
            m_Loop.Machine.StateChanged += OnWaveStateChanged;
            m_Subscribed = true;
        }

        void OnDestroy()
        {
            if (m_Subscribed && m_Loop != null && m_Loop.Machine != null)
                m_Loop.Machine.StateChanged -= OnWaveStateChanged;
            if (m_Rules != null)
            {
                m_Rules.Ended -= OnRuleEnded;
                m_Rules.EndAll();
            }
        }

        void Update()
        {
            // WaveTime does not move in WavePaused, so the remaining time of a boost is kept.
            if (m_Loop != null && m_Loop.Machine != null && m_Loop.State.IsInWave())
                m_Rules.Tick(Now);
        }

        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            if (next == WaveState.WaveIncoming)
                m_Rules.ResetWave();
            else if (!next.IsInWave())
                m_Rules.EndAll();
        }

        /// <summary>The need of the building is BOTTLENECK, or the Sudden rush of this wave is at the building.</summary>
        public bool IsOverloaded(Building building)
        {
            if (building == null) return false;
            if (m_Loop != null && m_Loop.SuddenRushBuilding == building) return true;
            return m_Board != null && m_Board.StateOf(building.Need) == NeedState.Bottleneck;
        }

        public SpeedUpBlock BlockOf(Building building)
        {
            if (building == null || building.Queue == null) return SpeedUpBlock.NotOverloaded;
            return m_Rules.BlockOf(building.Queue, IsWaveActive, IsOverloaded(building), Energy);
        }

        public bool CanSpeedUp(Building building) => BlockOf(building) == SpeedUpBlock.None;

        public bool IsActive(Building building) => building != null && building.Queue != null && m_Rules.IsActive(building.Queue);

        public float RemainingSeconds(Building building) =>
            building != null && building.Queue != null ? m_Rules.RemainingSeconds(building.Queue, Now) : 0f;

        public int MissingEnergy => m_Rules.MissingEnergy(Energy);

        /// <summary>Buildings the game offers the boost for now (also those the player cannot afford yet).</summary>
        public IReadOnlyList<Building> Offers()
        {
            m_Offers.Clear();
            if (m_Loop == null || m_Loop.Workers == null || !IsWaveActive) return m_Offers;
            foreach (var building in m_Loop.Workers.Buildings)
            {
                if (building == null || building.Queue == null) continue;
                if (m_Rules.IsOffered(building.Queue, true, IsOverloaded(building), Energy))
                    m_Offers.Add(building);
            }
            return m_Offers;
        }

        /// <summary>Spends energy_cost Energy and starts the boost; false (and nothing changes) when blocked.</summary>
        public bool TrySpeedUp(Building building)
        {
            var block = BlockOf(building);
            if (block != SpeedUpBlock.None)
            {
                Debug.Log($"Wave {m_Loop.Wave}: Speed Up at {Name(building)} blocked ({block}).");
                return false;
            }
            if (!m_Balances.EnergyWallet.TrySpend(m_Rules.EnergyCost, $"Speed Up {building.Need}"))
                return false;

            m_Rules.Start(building.Queue, Now);
            Debug.Log($"Wave {m_Loop.Wave}: Speed Up at {building.name} for {m_Rules.DurationSeconds:0} s " +
                      $"(service time ×{m_Rules.ServiceTimeMultiplier:0.##})");
            Started?.Invoke(building);
            return true;
        }

        void OnRuleEnded(BuildingQueue queue)
        {
            var building = FindBuilding(queue);
            if (m_Loop != null)
                Debug.Log($"Wave {m_Loop.Wave}: Speed Up at {Name(building)} ended");
            if (building != null)
                Ended?.Invoke(building);
        }

        Building FindBuilding(BuildingQueue queue)
        {
            if (m_Loop == null || m_Loop.Workers == null) return null;
            foreach (var building in m_Loop.Workers.Buildings)
            {
                if (building != null && building.Queue == queue) return building;
            }
            return null;
        }

        static string Name(Building building) => building != null ? building.name : "unknown building";
    }
}
