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
        PurchaseRules m_Rules;

        public event Action<Building> Upgraded;
        public event Action<bool> AvailabilityChanged;

        public WaveState WaveState => waveLoop != null && waveLoop.Machine != null ? waveLoop.State : WaveState.AwaitingWave;

        public bool IsUpgradeAvailable => m_Rules.IsShopOpen;

        void Awake()
        {
            if (waveLoop == null)
                waveLoop = FindAnyObjectByType<WaveLoop>();
            if (balances == null)
                balances = FindAnyObjectByType<ResortBalances>();
            m_Rules = new PurchaseRules(() => balances.CoinWallet, () => WaveState);
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

        public PurchaseOffer OfferFor(Building building) => new PurchaseOffer(
            $"upgrade {building.Need} to L{building.Level + 1}",
            PriceOf(building),
            building.IsMaxLevel ? PurchaseBlock.MaxLevel : PurchaseBlock.None);

        public int MissingCoins(Building building) => m_Rules.MissingCoins(OfferFor(building));

        public PurchaseBlock UpgradeBlockOf(Building building) => m_Rules.BlockOf(OfferFor(building));

        public bool CanUpgrade(Building building) => m_Rules.CanBuy(OfferFor(building));

        public bool TryUpgrade(Building building)
        {
            if (!m_Rules.TryBuy(OfferFor(building), building.LevelUp)) return false;
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
