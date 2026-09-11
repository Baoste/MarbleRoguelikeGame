namespace MarblesECS
{
    /// <summary>A detached projection of the campaign's ECS components.</summary>
    public struct SessionSnapshot
    {
        public bool IsCampaign;
        public RoundPhase Phase;
        public int StageIndex;
        public uint StageId;
        public int StageCount;
        public long TotalScore;
        public long ConfirmedScore;
        public long PendingScore;
        public long RoundScore;
        public long TargetScore;
        public long Coins;
        public long GamblingPayout;
        public long LastRewardCoins;
        public long LastCashoutCoins;
        public int StagesCleared;
        public int LastSkippedStages;
        public int ShopRefreshCount;
        public int LayoutRevision;
        public bool GamblingResolved;
        public bool GamblingWon;
        public bool CanCashOut;
    }
}
