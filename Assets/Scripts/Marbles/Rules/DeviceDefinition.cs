using System;

namespace MarblesECS
{
    public enum DeviceRarity { Common, Rare, Epic, Legendary }
    [Serializable]
    public sealed class DeviceDefinition
    {
        public uint Id;
        public string Name;
        public long Price;
        [OptionalBalanceField] public long SellPrice;
        [OptionalBalanceField] public DeviceRarity Rarity;
        [OptionalBalanceField] public int Durability = 20;
        public double ScoreMultiplier = 1;
        public double RushChanceAdd;
        // Runtime-only geometry from the package's placement SphereCollider; never serialized as business data.
        public float PlacementRadius { get; private set; }
        public float PlacementOffsetX { get; private set; }
        public float PlacementOffsetY { get; private set; }
        public float PlacementOffsetZ { get; private set; }
        public int StartingCount;
        [OptionalBalanceField] public int PurchaseQuantity = 1;
        [OptionalBalanceField] public bool RushCategory;
        [OptionalBalanceField] public string LinkGroup;
        [OptionalBalanceField] public float GroupClearance = .01f;
        // Historical category retained for offline balance reports; never dispatches device behaviour.
        [OptionalBalanceField] public DeviceKind Kind;
        [OptionalBalanceField] public string SourceId;
        [OptionalBalanceField] public string Description;
        [OptionalBalanceField] public float Cooldown = 0.1f;
        [OptionalBalanceField] public int MaxTriggersPerBall;
        [OptionalBalanceField] public float Range = 0.2f;
        [OptionalBalanceField] public float Strength = 2;
        [OptionalBalanceField] public double Value = 1;

        public DeviceDefinition Copy() { return (DeviceDefinition)MemberwiseClone(); }

        public void SetPlacementSphere(float x, float y, float z, float radius)
        {
            if (!MarbleRules.IsFinite(x) || !MarbleRules.IsFinite(y) || !MarbleRules.IsFinite(z) ||
                !MarbleRules.IsFinite(radius) || radius <= 0 || radius > 100)
                throw new ArgumentException("Invalid placement SphereCollider geometry.");
            PlacementOffsetX = x; PlacementOffsetY = y; PlacementOffsetZ = z; PlacementRadius = radius;
        }

        public void PlacementCenter(float x, float z, float angle, out float centerX, out float centerZ)
        {
            double radians = angle * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
            centerX = x + (float)(cos * PlacementOffsetX + sin * PlacementOffsetZ);
            centerZ = z + (float)(-sin * PlacementOffsetX + cos * PlacementOffsetZ);
        }

        public void Validate()
        {
            BalanceGuard.Range(Id, 1, uint.MaxValue, nameof(Id));
            BalanceGuard.Text(Name, nameof(Name));
            BalanceGuard.Range(Price, 1, BalanceGuard.MaxInteger, nameof(Price));
            BalanceGuard.Range(SellPrice, 0, BalanceGuard.MaxInteger, nameof(SellPrice));
            if (Durability != -1) BalanceGuard.Range(Durability, 1, 10000, nameof(Durability));
            if (!Enum.IsDefined(typeof(DeviceRarity), Rarity)) throw new ArgumentException("Invalid device rarity.");
            BalanceGuard.Range(StartingCount, 0, 200, nameof(StartingCount));
            BalanceGuard.Range(PurchaseQuantity, 1, 20, nameof(PurchaseQuantity));
            BalanceGuard.Range(GroupClearance, 0, 1, nameof(GroupClearance));
            BalanceGuard.Range(RushChanceAdd, 0, 1, nameof(RushChanceAdd));
            BalanceGuard.Range(Cooldown, 0, 60, nameof(Cooldown));
            BalanceGuard.Range(Range, 0.01, 100, nameof(Range));
            BalanceGuard.Range(Strength, 0, 1000, nameof(Strength));
            BalanceGuard.Range(MaxTriggersPerBall, 0, 10000, nameof(MaxTriggersPerBall));
            BalanceGuard.Range(ScoreMultiplier, 1, 100, nameof(ScoreMultiplier));
            if (double.IsNaN(Value) || double.IsInfinity(Value)) throw new ArgumentException("Device Value must be finite.");
        }
    }
}
