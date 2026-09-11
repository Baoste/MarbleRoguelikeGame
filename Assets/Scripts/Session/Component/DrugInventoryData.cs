using Unity.Entities;

namespace MarblesECS
{
    internal struct DrugInventoryData : IBufferElementData
    {
        public uint DefinitionId;
        public int Count;
    }
}
