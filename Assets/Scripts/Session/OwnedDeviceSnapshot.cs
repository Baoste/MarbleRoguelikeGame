namespace MarblesECS
{
    public readonly struct DeviceBrokenEvent
    {
        public int RoundId { get; }
        public int InstanceId { get; }
        public uint DefinitionId { get; }

        internal DeviceBrokenEvent(int roundId, int instanceId, uint definitionId)
        {
            RoundId = roundId;
            InstanceId = instanceId;
            DefinitionId = definitionId;
        }
    }

    public struct OwnedDeviceSnapshot
    {
        public int InstanceId;
        public uint DefinitionId;
        public string Name;
        public bool Placed;
        public float X;
        public float Z;
        public float Radius;
        public float PlacementX, PlacementZ;
        public float PlacementOffsetX, PlacementOffsetY, PlacementOffsetZ;
        public long PurchasePrice;
        public long SellPrice;
        public DeviceRarity Rarity;
        public int Durability, RemainingDurability;
        public bool CompletingEffect;
        public bool Exhausted => RemainingDurability == 0;
        public double ScoreMultiplier;
        public double RushChanceAdd;
        public DeviceKind Kind;
        public string Description;
        public bool RotationLocked;
        public float Angle, Range, Strength;
        public int PairId;
    }
}
