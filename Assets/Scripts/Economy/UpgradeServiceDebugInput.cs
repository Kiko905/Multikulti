using Multikulti.Buildings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Multikulti.Economy
{
    [RequireComponent(typeof(UpgradeService))]
    public sealed class UpgradeServiceDebugInput : MonoBehaviour
    {
        UpgradeService m_Service;

        void Start() => m_Service = GetComponent<UpgradeService>();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.uKey.wasPressedThisFrame) return;

            foreach (var building in FindObjectsByType<Building>(FindObjectsSortMode.None))
            {
                var price = m_Service.PriceOf(building);
                if (m_Service.TryUpgrade(building))
                    Debug.Log($"{building.Need}: upgrade to L{building.Level} for {price} Coins, capacity {building.Queue.Capacity}");
                else
                    Debug.Log($"{building.Need}: no upgrade (L{building.Level}, price {price})");
            }
        }
    }
}
