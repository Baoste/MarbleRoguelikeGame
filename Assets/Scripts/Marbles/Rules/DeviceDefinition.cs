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

        public DeviceDefinition Copy() { return (DeviceDefinition)MemberwiseClone(); }

        public void Validate()
        {
            BalanceGuard.Range(Id, 1, uint.MaxValue, nameof(Id));
            BalanceGuard.Text(Name, nameof(Name));
            BalanceGuard.Range(Price, 1, BalanceGuard.MaxInteger, nameof(Price));
            BalanceGuard.Range(Radius, 0.05, 0.5, nameof(Radius));
            BalanceGuard.Range(StartingCount, 0, 200, nameof(StartingCount));
            BalanceGuard.Range(RushChanceAdd, 0, 1, nameof(RushChanceAdd));
            if (!((ScoreMultiplier == 2 && RushChanceAdd == 0) || (ScoreMultiplier == 1 && RushChanceAdd > 0)))
                throw new ArgumentException("Devices must be either score x2 or Rush chance pins.");
        }
    }
}
