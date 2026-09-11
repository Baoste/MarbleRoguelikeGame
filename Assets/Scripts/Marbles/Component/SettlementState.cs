using Unity.Entities;

namespace MarblesECS
{
    internal struct SettlementState : IComponentData
    {
        public bool IsSettled;
    }
}
