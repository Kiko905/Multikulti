using System;
using Multikulti.Waves;

namespace Multikulti.Economy
{
    public sealed class PurchaseRules
    {
        readonly Func<CurrencyWallet> m_Wallet;
        readonly Func<WaveState> m_WaveState;

        public PurchaseRules(Func<CurrencyWallet> wallet, Func<WaveState> waveState)
        {
            m_Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            m_WaveState = waveState ?? throw new ArgumentNullException(nameof(waveState));
        }

        public bool IsShopOpen => m_WaveState().IsBetweenWaves();

        public PurchaseBlock BlockOf(in PurchaseOffer offer)
        {
            if (!IsShopOpen) return PurchaseBlock.BetweenWavesOnly;
            if (offer.ItemBlock != PurchaseBlock.None) return offer.ItemBlock;
            if (!m_Wallet().CanAfford(offer.Price)) return PurchaseBlock.NotEnoughCoins;
            return PurchaseBlock.None;
        }

        public bool CanBuy(in PurchaseOffer offer) => BlockOf(offer) == PurchaseBlock.None;

        public int MissingCoins(in PurchaseOffer offer) => offer.IsOwned ? 0 : m_Wallet().Missing(offer.Price);

        public bool TryBuy(in PurchaseOffer offer, Action deliver)
        {
            if (deliver == null) throw new ArgumentNullException(nameof(deliver));
            if (!CanBuy(offer)) return false;
            if (!m_Wallet().TrySpend(offer.Price, offer.Item)) return false;
            deliver();
            return true;
        }
    }
}
