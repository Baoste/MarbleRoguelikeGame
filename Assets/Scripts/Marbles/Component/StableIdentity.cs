using Unity.Entities;

namespace MarblesECS
{
    internal struct StableIdentity : IComponentData
    {
        public ulong StableId;
    }
}
