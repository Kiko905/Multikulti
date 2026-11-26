using Multikulti.Buildings;
using Multikulti.Config;
using Multikulti.Waves;

namespace Multikulti.Economy
{
    /// <summary>
    /// How much Resort Value things are worth (SCRUM-56, build document §5.5 "zdroj: budovy, upgrady, hodnotenia"),
    /// pure C#. Resort Value = value of every building + value of every finished wave:
    /// - a building is worth resort_value.building_base, a bought area (Pool, Bungalows) its unlock price;
    /// - every upgrade adds its Coins price (buildings.upgrade_prices), so investing always raises the status;
    /// - every finished wave adds resort_value.per_rating of its rating.
    /// Nothing is ever subtracted, so Resort Value only grows.
    /// </summary>
    public static class ResortValueRules
    {
        /// <summary>Value of a level 1 building of the need.</summary>
        public static int BaseValue(Need need, WaveConfig config) => need switch
        {
            Need.Relax => config.Unlocks.PoolPrice,
            Need.OceanView => config.Unlocks.BungalowsPrice,
            _ => config.ResortValue.BuildingBase
        };

        /// <summary>Value of a building of the need at the level: base value plus every upgrade up to the level.</summary>
        public static int BuildingValue(Need need, int level, WaveConfig config)
        {
            var value = BaseValue(need, config);
            var prices = config.Buildings.UpgradePrices;
            for (var i = 0; i < level - 1 && i < prices.Count; i++)
                value += prices[i];
            return value;
        }

        /// <summary>Resort Value the upgrade from the level to the next one adds; 0 at max level.</summary>
        public static int UpgradeGain(int level, WaveConfig config)
        {
            var prices = config.Buildings.UpgradePrices;
            var index = level - 1;
            return index >= 0 && index < prices.Count ? prices[index] : 0;
        }

        /// <summary>Resort Value a finished wave with the rating adds; a missing value counts as 0.</summary>
        public static int RatingValue(WaveRating rating, WaveConfig config)
        {
            var values = config.ResortValue.PerRating;
            var index = (int)rating - 1;
            return index >= 0 && index < values.Count ? values[index] : 0;
        }
    }
}
