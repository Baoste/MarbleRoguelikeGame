using Unity.Entities;

namespace MarblesECS
{
    internal struct CampaignData : IComponentData
    {
        public bool RepeatRounds;
        public ProductionSettings Production;
        public long CompanyScripBalance, LastCompanyScripReward;
        // Compatibility economy for standalone production and version 2 Day fixtures.
        public long RewardCoins;
        // Existing sessions leave this mask at zero. Day definitions opt in to each feature.
        public ProductionFeature DisabledFeatures;
        public long TotalScore;
        public long Coins;
        public int NextDeviceId;
        public int LayoutRevision;
        public long LastRewardCoins;
        public long LastCashoutCoins;
        public int ShopRefreshCount;
    }
}
