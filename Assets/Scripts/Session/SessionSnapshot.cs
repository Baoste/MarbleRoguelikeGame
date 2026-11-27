namespace MarblesECS
{
    /// <summary>A detached projection of the campaign's ECS components.</summary>
    public struct SessionSnapshot
    {
        public bool IsCampaign;
        public RoundPhase Phase;
        public long TotalScore;
        public long ConfirmedScore;
        public long RoundScore;
        public long TargetScore;
        public ProductionSettings Production;
        public long CompanyScripBalance, BiomassScrapBalance, LastCompanyScripReward;
        public int ShopRefreshCount;
        public int LayoutRevision;
        public bool CanCashOut;
    }
}
