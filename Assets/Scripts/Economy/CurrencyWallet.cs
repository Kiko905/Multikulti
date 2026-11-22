using System;

namespace Multikulti.Economy
{
    public sealed class CurrencyWallet
    {
        public event Action<int, int, string> Changed;

        public CurrencyWallet(int startBalance = 0)
        {
            if (startBalance < 0)
                throw new ArgumentOutOfRangeException(nameof(startBalance), startBalance, "Start balance must not be negative.");
            Balance = startBalance;
        }

        public int Balance { get; private set; }

        public bool CanAfford(int price) => Missing(price) == 0;

        public int Missing(int price)
        {
            RequireNotNegative(price, nameof(price));
            return Math.Max(0, price - Balance);
        }

        public void Add(int amount, string reason)
        {
            RequireNotNegative(amount, nameof(amount));
            if (amount == 0) return;
            SetBalance(checked(Balance + amount), reason);
        }

        public bool TrySpend(int price, string reason)
        {
            if (!CanAfford(price)) return false;
            if (price == 0) return true;
            SetBalance(Balance - price, reason);
            return true;
        }

        void SetBalance(int value, string reason)
        {
            var old = Balance;
            Balance = value;
            Changed?.Invoke(old, value, reason);
        }

        static void RequireNotNegative(int value, string name)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(name, value, "Amount must not be negative.");
        }
    }
}