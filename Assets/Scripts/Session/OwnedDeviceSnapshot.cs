namespace MarblesECS
{
    public struct OwnedDeviceSnapshot
    {
        public int InstanceId;
        public uint DefinitionId;
        public string Name;
        public bool Placed;
        public float X;
        public float Z;
        public float Radius;
        public long PurchasePrice;
        public long SellPrice;
        public double ScoreMultiplier;
        public double RushChanceAdd;
        public DeviceKind Kind;
        public string Description;
        public float Angle, Range, Strength;
        public int PairId;
    }
}
