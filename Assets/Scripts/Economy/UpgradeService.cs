using System;
using Multikulti.Buildings;
using Multikulti.Config;
using UnityEngine;

namespace Multikulti.Economy
{
    public sealed class UpgradeService : MonoBehaviour
    {
        [SerializeField] ResortBalances balances;

        public event Action<Building> Upgraded;

        public int PriceOf(Building building) =>
            building.IsMaxLevel ? 0 : WaveConfig.Current.Buildings.UpgradePrices[building.Level - 1];

        public int MissingCoins(Building building) =>
            building.IsMaxLevel ? 0 : balances.CoinWallet.Missing(PriceOf(building));
        public bool CanUpgrade(Building building) =>
            !building.IsMaxLevel && balances.CoinWallet.CanAfford(PriceOf(building));
        public bool TryUpgrade(Building building)
        {
            if (building.IsMaxLevel) return false;
            if (!balances.CoinWallet.TrySpend(PriceOf(building), $"upgrade {building.Need} to L{building.Level + 1}"))
            return false;
            building.LevelUp();
            Upgraded?.Invoke(building);
            return true;
        }
    }
}
