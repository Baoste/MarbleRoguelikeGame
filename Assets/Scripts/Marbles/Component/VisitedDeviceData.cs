using Unity.Entities;

namespace MarblesECS
{
    // Board pin and zone effects apply once per marble.
    internal struct VisitedDeviceData : IBufferElementData
    {
        public ContactTargetKey Key;
    }
}
