using Unity.Entities;

namespace MarblesECS
{
    internal struct CampaignData : IComponentData
    {
        public bool RepeatRounds;
        public ProductionSettings Production;
        public long CompanyScripBalance, LastCompanyScripReward;
        // Existing sessions leave this mask at zero. Day definitions opt in to each feature.
        public ProductionFeature DisabledFeatures;
        public long TotalScore;
        public int NextDeviceId;
        public int LayoutRevision;
        public int ShopRefreshCount;
    }
}
