using System;
using UnityEngine;

namespace Multikulti.Economy
{
    /// <summary>
    /// Balances shown in the top bar of the Home screen (SCRUM-21): Coins, Energy and Resort Value.
    /// The prototype has only these two currencies plus the Resort Value status (build document §2);
    /// Gems, Materials and Reputation come in the MVP.
    ///
    /// This component only holds the numbers. Where they come from (wave rewards, prices, Energy sources)
    /// belongs to SCRUM-45, SCRUM-55, SCRUM-56 and SCRUM-59. The start values can be set in the inspector,
    /// and an inspector change in Play mode shows up in the top bar at once.
    /// </summary>
    public sealed class ResortBalances : MonoBehaviour
    {
        [SerializeField, Min(0)] int coins;
        [SerializeField, Min(0)] int energy;
        [SerializeField, Min(0), Tooltip("Status of the resort; it only grows.")]
        int resortValue;

        /// <summary>Raised after any balance changed.</summary>
        public event Action Changed;

        public int Coins
        {
            get => coins;
            set => Set(ref coins, Mathf.Max(0, value));
        }

        public int Energy
        {
            get => energy;
            set => Set(ref energy, Mathf.Max(0, value));
        }

        /// <summary>Resort Value only grows (build document §5.5); a lower value is ignored with a warning.</summary>
        public int ResortValue
        {
            get => resortValue;
            set
            {
                if (value < resortValue)
                {
                    Debug.LogWarning($"Resort Value only grows: {value} < {resortValue}, ignored.", this);
                    return;
                }
                Set(ref resortValue, value);
            }
        }

        void Set(ref int field, int value)
        {
            if (field == value) return;
            field = value;
            Changed?.Invoke();
        }

        void OnValidate() => Changed?.Invoke();
    }
}
