using Unity.Entities;

namespace MarblesECS
{
    internal struct MarbleModifier : IBufferElementData
    {
        public uint StackKey;
        public MarbleModifierAttribute Attribute;
        public double Value;
        public ushort Stacks;
        public long ExpireTick; // Zero lasts until this marble leaves the round.
    }
}
