using System;
using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Waves;
using UnityEngine;

namespace Multikulti.Economy
{
    public sealed class UpgradeService : MonoBehaviour
    {
        [SerializeField] ResortBalances balances;
        [SerializeField] WaveLoop waveLoop;

        bool m_Subscribed;
        public event Action<Building> Upgraded;
        public event Action<bool> AvailabilityChanged;

        public WaveState WaveState => waveLoop != null && waveLoop.Machine != null ? waveLoop.State : WaveState.AwaitingWave;

        public bool IsUpgradeAvailable => WaveState.IsBetweenWaves();

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
        }

        void Start()
        {
            if (waveLoop == null || waveLoop.Machine == null) return;
            waveLoop.Machine.StateChanged += OnWaveStateChanged;
            m_Subscribed = true;
        }

        void OnDestroy()
        {
            if (m_Subscribed && waveLoop != null && waveLoop.Machine != null)
                waveLoop.Machine.StateChanged -= OnWaveStateChanged;
        }
        public int PriceOf(Building building) =>
            building.IsMaxLevel ? 0 : WaveConfig.Current.Buildings.UpgradePrices[building.Level - 1];

        public int MissingCoins(Building building) =>
            building.IsMaxLevel ? 0 : balances.CoinWallet.Missing(PriceOf(building));

        public UpgradeBlock UpgradeBlockOf(Building building)
        {
            if (!IsUpgradeAvailable) return UpgradeBlock.BetweenWavesOnly;
            if (building.IsMaxLevel) return UpgradeBlock.MaxLevel;
            if (!balances.CoinWallet.CanAfford(PriceOf(building))) return UpgradeBlock.NotEnoughCoins;
            return UpgradeBlock.None;
        }

        public bool CanUpgrade(Building building) => UpgradeBlockOf(building) == UpgradeBlock.None;

        public bool TryUpgrade(Building building)
        {
            if (UpgradeBlockOf(building) != UpgradeBlock.None) return false;
            if (!balances.CoinWallet.TrySpend(PriceOf(building), $"upgrade {building.Need} to L{building.Level + 1}"))
            return false;
            building.LevelUp();
            Upgraded?.Invoke(building);
            return true;
        }
        void OnWaveStateChanged(WaveState previous, WaveState next)
        {
            if (previous.IsBetweenWaves() != next.IsBetweenWaves())
                AvailabilityChanged?.Invoke(next.IsBetweenWaves());
        }
    }
}
