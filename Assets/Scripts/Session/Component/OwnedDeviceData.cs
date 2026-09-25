using Unity.Entities;

namespace MarblesECS
{
    internal struct OwnedDeviceData : IComponentData
    {
        public int InstanceId;
        public uint DefinitionId;
        public long PurchasePrice;
        public bool Placed;
        public float X;
        public float Z;
        public float Angle;
        public int PairId;
        public double NextTriggerAt, StoredValue;
    }
}
