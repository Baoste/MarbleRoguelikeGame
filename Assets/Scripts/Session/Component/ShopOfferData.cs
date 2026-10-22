using Unity.Entities;

namespace MarblesECS
{
    public enum ShopItemKind : byte { Device }

    internal struct ShopOfferData : IBufferElementData
    {
        public ShopItemKind Kind;
        public uint DefinitionId;
        public long Price;
        public bool Sold;
    }
}
