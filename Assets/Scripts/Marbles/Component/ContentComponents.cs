using Unity.Entities;

namespace MarblesECS
{
    internal struct AttributeValue : IBufferElementData { public double Base; public double Current; }
    internal struct AttributeModifierData : IBufferElementData
    {
        public uint Source;
        public AttributeEffect Effect;
        public long ExpireTick;
    }
    internal struct ActiveDrugData : IBufferElementData
    {
        public uint DefinitionId;
        public double EndsAt;
    }
    internal struct BallFeatures : IComponentData
    {
        public ulong Family;
        public int Generation;
        public int PiercesUsed;
        public long BirthTick;
        public bool Replicates, Magnetic, ExitDown, Revive, Clover, SplitConsumed;
        public double RushChanceAdd, RushScoreMultiplier, PortalLockedUntil;
        public float PierceChance, MagneticRadius;
        public float RefundBlood;
        public long MovedTick;
    }
    internal struct DeviceVisit : IBufferElementData
    {
        public long Key;
        public int Count;
        public long LastTick;
    }
}
