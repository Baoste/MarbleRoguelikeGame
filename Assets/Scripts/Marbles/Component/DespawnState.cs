using Unity.Entities;

namespace MarblesECS
{
    internal struct DespawnState : IComponentData
    {
        public bool PendingDespawn;
        public MarbleDespawnReason Reason;
    }
}
