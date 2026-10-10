namespace MarblesECS
{
    internal static class GameBalanceDefaults
    {
        internal static GameBalance Create()
        {
            return new GameBalance
            {
                Marble = new MarbleTuning
                {
                    BaseRushChance = 0.06, BaseScore = 10, TargetScore = 350,
                    FireIntervalSeconds = 0.3f, LaunchSpeed = 5, RushExtendsDuration = false
                },
                Campaign = new CampaignTuning(),
                Stages = new[]
                {
                    Stage(1, 150, 35, 18, 1), Stage(2, 450, 45, 22, 1.05),
                    Stage(3, 960, 60, 30, 1.1), Stage(4, 1650, 80, 40, 1.2),
                    Stage(5, 2520, 105, 52, 1.3), Stage(6, 3750, 135, 68, 1.4)
                },
                Devices = System.Array.Empty<DeviceDefinition>(),
                Drugs = new[]
                {
                    new DrugDefinition { Id = 1, Name = "弹性剂", Price = 10, RestitutionMultiplier = 1.5f, StartingCount = 1 },
                    new DrugDefinition { Id = 2, Name = "缓弹剂", Price = 8, RestitutionMultiplier = 0.65f, StartingCount = 1 }
                }
            };
        }

        private static StageDefinition Stage(uint id, long score, long reward, long skip, double rate)
        {
            return new StageDefinition { Id = id, TargetScore = score, RewardCoins = reward, SkipRewardCoins = skip, CashoutRate = rate };
        }
    }
}
