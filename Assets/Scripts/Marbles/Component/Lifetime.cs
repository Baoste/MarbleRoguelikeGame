using Unity.Entities;

namespace MarblesECS
{
    internal struct Lifetime : IComponentData
    {
        public long ExpireTick; // Zero means no timed expiry.
    }
}
