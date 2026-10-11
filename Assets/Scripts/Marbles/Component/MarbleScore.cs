using Unity.Entities;

namespace MarblesECS
{
    internal struct MarbleScore : IComponentData
    {
        public double BaseScore;
        public double ScoreBonus;
        public double ScoreMultiplier;
        public double BaseMultiplier;
        public double DeviceMultiplierBonus;

        internal void AddDeviceMultiplier(double amount)
        {
            if (!MarbleRules.IsFinite(amount) || amount < 0) throw new System.ArgumentOutOfRangeException(nameof(amount));
            // API callers provide the full factor; only its offset from neutral x1 accumulates.
            DeviceMultiplierBonus = System.Math.Min(999d, DeviceMultiplierBonus + (amount - 1d));
            ScoreMultiplier = System.Math.Min(1000d, BaseMultiplier * System.Math.Max(0d, 1d + DeviceMultiplierBonus));
        }
    }
}
