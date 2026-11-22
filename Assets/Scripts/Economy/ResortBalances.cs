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
        CurrencyWallet m_CoinWallet;

        /// <summary>Raised after any balance changed.</summary>
        public event Action Changed;

        public CurrencyWallet CoinWallet
        {
            get
            {
                if (m_CoinWallet == null)
                {
                    m_CoinWallet = new CurrencyWallet(Mathf.Max(0, coins));
                    m_CoinWallet.Changed += OnCoinsChanged;
                }
                return m_CoinWallet;
            }
        }

        public int Coins => CoinWallet.Balance;

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

        void OnCoinsChanged(int oldBalance, int newBalance, string reason)
        {
            coins = newBalance;
            if (Debug.isDebugBuild)
                Debug.Log($"Coins {newBalance - oldBalance:+#;-#;0} ({reason}): {oldBalance} → {newBalance}", this);
            Changed?.Invoke();
        }

        void OnValidate()
        {
            if (Application.isPlaying && m_CoinWallet != null && coins != m_CoinWallet.Balance)
            {
                var delta = coins - m_CoinWallet.Balance;
                if (delta > 0) m_CoinWallet.Add(delta, "inspector");
                else m_CoinWallet.TrySpend(-delta, "inspector");
                return;
            }
            Changed?.Invoke();
        }
    }
}
