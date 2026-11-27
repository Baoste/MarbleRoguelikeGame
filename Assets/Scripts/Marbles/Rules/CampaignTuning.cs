using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class CampaignTuning
    {
        public double SellRatio = 0.5;
        public int MaxOwnedDevices = 20;
        public long MaxTotalScore = 9000000000000000L;

        public CampaignTuning Copy() { return (CampaignTuning)MemberwiseClone(); }

        public void Validate()
        {
            BalanceGuard.Range(SellRatio, 0, 1, nameof(SellRatio));
            BalanceGuard.Range(MaxOwnedDevices, 1, 200, nameof(MaxOwnedDevices));
            BalanceGuard.Range(MaxTotalScore, 1, BalanceGuard.MaxInteger, nameof(MaxTotalScore));
        }
    }
}
