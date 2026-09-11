using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class CampaignTuning
    {
        public long StartingCoins = 30;
        public double CashoutCoinsPerBlood = 0.5;
        public long ShopRefreshCost = 5;
        public int ShopOfferCount = 4;
        public double SellRatio = 0.5;
        public int MaxOwnedDevices = 20;
        public int MaxDrugInventory = 30;
        public double GamblingWinChance = 0.5;
        public double GamblingWinMultiplier = 3;
        public long MaxTotalScore = 9000000000000000L;

        public CampaignTuning Copy() { return (CampaignTuning)MemberwiseClone(); }

        public void Validate()
        {
            BalanceGuard.Range(StartingCoins, 0, BalanceGuard.MaxInteger, nameof(StartingCoins));
            BalanceGuard.Range(CashoutCoinsPerBlood, 0, 1000000, nameof(CashoutCoinsPerBlood));
            BalanceGuard.Range(ShopRefreshCost, 1, BalanceGuard.MaxInteger, nameof(ShopRefreshCost));
            BalanceGuard.Range(ShopOfferCount, 1, 12, nameof(ShopOfferCount));
            BalanceGuard.Range(SellRatio, 0, 1, nameof(SellRatio));
            BalanceGuard.Range(MaxOwnedDevices, 1, 200, nameof(MaxOwnedDevices));
            BalanceGuard.Range(MaxDrugInventory, 1, 1000, nameof(MaxDrugInventory));
            BalanceGuard.Range(GamblingWinChance, 0, 1, nameof(GamblingWinChance));
            BalanceGuard.Range(GamblingWinMultiplier, 1, 1000, nameof(GamblingWinMultiplier));
            BalanceGuard.Range(MaxTotalScore, 1, BalanceGuard.MaxInteger, nameof(MaxTotalScore));
        }
    }
}
