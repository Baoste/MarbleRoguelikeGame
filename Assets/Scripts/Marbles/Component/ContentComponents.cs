using Unity.Entities;

namespace MarblesECS
{
    internal struct AttributeValue : IBufferElementData { public double Base; public double Current; }
    internal struct AttributeModifierData : IBufferElementData
    {
        public uint Source;
        public uint RuleHandle;
        public AttributeEffect Effect;
        public long ExpireTick;
    }

    internal struct BallFeatures : IComponentData
    {
        public ulong Family;
        public int SpawnSourceId;
        public int FirstTriggeredDeviceId;
        public bool TriggeredMultipleDevices;
        public int Generation;
        public int ReplicationGeneration;
        public int PiercesUsed;
        public long BirthTick;
        public bool Magnetic, ExitDown, Revive, SplitConsumed;
        public double RushChanceAdd, PortalLockedUntil;
        public float PierceChance, MagneticRadius, CloverChance;
        public float RefundBlood;
        public long MovedTick;
    }
    internal struct DeviceVisit : IBufferElementData
    {
        public DeviceVisitKey Key;
        public long LastTick;
        public int Count;
        public bool InheritOnCopy;
    }
    internal struct DeviceSplitSource : IBufferElementData { public int InstanceId; }
    internal struct DeviceCopySource : IBufferElementData { public int InstanceId; }
}
