namespace Multikulti.Economy
{
    public readonly struct PurchaseOffer
    {
        public readonly string Item;
        public readonly int Price;
        public readonly PurchaseBlock ItemBlock;

        public PurchaseOffer(string item, int price, PurchaseBlock itemBlock = PurchaseBlock.None)
        {
            Item = item;
            Price = price;
            ItemBlock = itemBlock;
        }

        public bool IsOwned => ItemBlock == PurchaseBlock.MaxLevel || ItemBlock == PurchaseBlock.AlreadyOwned;
    }
}
