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

        public bool CanUpgrade(Building building) =>
            !building.IsMaxLevel && balances.Coins >= PriceOf(building);

        public bool TryUpgrade(Building building)
        {
            if (!CanUpgrade(building)) return false;
            balances.Coins -= PriceOf(building);
            building.LevelUp();
            Upgraded?.Invoke(building);
            return true;
        }
    }
}
