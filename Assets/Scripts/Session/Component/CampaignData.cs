using Unity.Entities;

namespace MarblesECS
{
    internal struct CampaignData : IComponentData
    {
        public int StageIndex;
        public int StagesCleared;
        public long TotalScore;
        public long Coins;
        public uint RandomState;
        public int NextDeviceId;
        public int LayoutRevision;
        public int LastSkippedStages;
        public long LastRewardCoins;
        public long LastCashoutCoins;
        public int ShopRefreshCount;
    }
}
