using Unity.Entities;

namespace MarblesECS
{
    internal struct CampaignData : IComponentData
    {
        public int StageIndex;
        // Zero selects the full legacy stage list. A production plan can bound it.
        public int StageEndExclusive;
        public int StageStartIndex;
        public long StageTargetOverride;
        // Existing sessions leave this mask at zero. Day definitions opt in to each feature.
        public ProductionFeature DisabledFeatures;
        public int StagesCleared;
        public long TotalScore;
        public long Coins;
        public int NextDeviceId;
        public int LayoutRevision;
        public int LastSkippedStages;
        public long LastRewardCoins;
        public long LastCashoutCoins;
        public int ShopRefreshCount;
    }
}
