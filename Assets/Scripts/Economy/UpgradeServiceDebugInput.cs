using Multikulti.Buildings;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.InputSystem;

namespace Multikulti.Economy
{
    [RequireComponent(typeof(UpgradeService))]
    public sealed class UpgradeServiceDebugInput : MonoBehaviour
    {
        UpgradeService m_Service;
        ResortBalances m_Balances;

        void Start()
        {
            m_Service = GetComponent<UpgradeService>();
            m_Balances = FindAnyObjectByType<ResortBalances>();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.cKey.wasPressedThisFrame && m_Balances != null)
                m_Balances.CoinWallet.Add(300, "debug key C");

            if (!keyboard.uKey.wasPressedThisFrame) return;

            foreach (var building in FindObjectsByType<Building>(FindObjectsSortMode.None))
            {
                var price = m_Service.PriceOf(building);
                if (m_Service.TryUpgrade(building))
                    Debug.Log($"{building.Need}: upgrade to L{building.Level} for {price} Coins, capacity {building.Queue.Capacity}");
                else if (building.IsMaxLevel)
                    Debug.Log($"{building.Need}: no upgrade, max level L{building.Level}");
                else
                    Debug.Log($"{building.Need}: no upgrade to L{building.Level + 1} (price {price}), " + MissingText(m_Service.MissingCoins(building)));
            }
        }

        static string MissingText(int missing) =>
            new LocalizedString("UI", "economy.missing_coins") { Arguments = new object[] { missing } }.GetLocalizedString();

    }
}
