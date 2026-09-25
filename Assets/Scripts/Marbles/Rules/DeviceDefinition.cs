using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class DeviceDefinition
    {
        public uint Id;
        public string Name;
        public long Price;
        public double ScoreMultiplier = 1;
        public double RushChanceAdd;
        public float Radius = 0.18f;
        public int StartingCount;
        [OptionalBalanceField] public DeviceKind Kind;
        [OptionalBalanceField] public string SourceId;
        [OptionalBalanceField] public string Description;
        [OptionalBalanceField] public float Cooldown = 0.1f;
        [OptionalBalanceField] public int MaxTriggersPerBall;
        [OptionalBalanceField] public float Range = 0.2f;
        [OptionalBalanceField] public float Strength = 2;
        [OptionalBalanceField] public double Value = 1;

        public DeviceDefinition Copy() { return (DeviceDefinition)MemberwiseClone(); }

        public void Validate()
        {
            BalanceGuard.Range(Id, 1, uint.MaxValue, nameof(Id));
            BalanceGuard.Text(Name, nameof(Name));
            BalanceGuard.Range(Price, 1, BalanceGuard.MaxInteger, nameof(Price));
            BalanceGuard.Range(Radius, 0.01, 0.5, nameof(Radius));
            BalanceGuard.Range(StartingCount, 0, 200, nameof(StartingCount));
            BalanceGuard.Range(RushChanceAdd, 0, 1, nameof(RushChanceAdd));
            BalanceGuard.Range(Cooldown, 0, 60, nameof(Cooldown));
            BalanceGuard.Range(Range, 0.01, 100, nameof(Range));
            BalanceGuard.Range(Strength, 0, 1000, nameof(Strength));
            BalanceGuard.Range(MaxTriggersPerBall, 0, 10000, nameof(MaxTriggersPerBall));
            if (Kind == DeviceKind.LegacyPin && !((ScoreMultiplier == 2 && RushChanceAdd == 0) || (ScoreMultiplier == 1 && RushChanceAdd > 0)))
                throw new ArgumentException("Devices must be either score x2 or Rush chance pins.");
        }
    }
}
