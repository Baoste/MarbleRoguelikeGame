using Unity.Entities;

namespace MarblesECS
{
    // Each marble can gain a given device's multiplier only once in its lifetime.
    internal struct VisitedDeviceData : IBufferElementData
    {
        public int DeviceId;
    }
}
