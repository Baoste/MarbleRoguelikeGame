using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class StageDefinition
    {
        public uint Id;
        public long TargetScore;
        public long RewardCoins;
        public long SkipRewardCoins;
        public double CashoutRate = 1;

        public StageDefinition Copy() { return (StageDefinition)MemberwiseClone(); }

        public void Validate()
        {
            BalanceGuard.Range(Id, 1, uint.MaxValue, nameof(Id));
            BalanceGuard.Range(TargetScore, 1, BalanceGuard.MaxInteger, nameof(TargetScore));
            BalanceGuard.Range(RewardCoins, 0, BalanceGuard.MaxInteger, nameof(RewardCoins));
            BalanceGuard.Range(SkipRewardCoins, 0, BalanceGuard.MaxInteger, nameof(SkipRewardCoins));
            BalanceGuard.Range(CashoutRate, 0, 1000, nameof(CashoutRate));
        }
    }
}
