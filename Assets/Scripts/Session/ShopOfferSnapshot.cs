namespace MarblesECS
{
    public struct ShopOfferSnapshot
    {
        public int Index;
        public ShopItemKind Kind;
        public uint DefinitionId;
        public string Name;
        public string Description;
        public long Price;
        public bool Sold;
        public DeviceRarity Rarity;
        public int Durability;
        public long SellPrice;
    }
}
