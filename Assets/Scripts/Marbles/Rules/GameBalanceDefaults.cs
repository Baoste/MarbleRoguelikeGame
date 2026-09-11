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
                    FireIntervalSeconds = 0.3f, LaunchSpeed = 5, RushExtendsDuration = true
                },
                Campaign = new CampaignTuning(), Board = new BoardTuning(),
                Stages = new[]
                {
                    Stage(1, 350, 30, 15, 1), Stage(2, 1000, 40, 20, 1.1),
                    Stage(3, 2200, 50, 25, 1.2), Stage(4, 4200, 60, 30, 1.3),
                    Stage(5, 7500, 75, 40, 1.4), Stage(6, 12000, 100, 50, 1.5)
                },
                Devices = new[]
                {
                    new DeviceDefinition { Id = 1, Name = "倍分钉", Price = 25, ScoreMultiplier = 2, StartingCount = 2 },
                    new DeviceDefinition { Id = 2, Name = "Rush 钉", Price = 20, RushChanceAdd = 0.08, StartingCount = 1 }
                },
                Drugs = new[]
                {
                    new DrugDefinition { Id = 1, Name = "弹性剂", Price = 10, RestitutionMultiplier = 1.5f, StartingCount = 1 },
                    new DrugDefinition { Id = 2, Name = "缓弹剂", Price = 8, RestitutionMultiplier = 0.65f, StartingCount = 1 }
                },
                Zones = new[]
                {
                    Zone(1, "落空", 2, -4, 0), Zone(2, "得分 x1", 0, -2, 1),
                    Zone(3, "赌分 x3", 1, 0, 1), Zone(4, "得分 x1.5", 0, 2, 1.5),
                    Zone(5, "落空", 2, 4, 0)
                }
            };
        }

        private static StageDefinition Stage(uint id, long score, long reward, long skip, double rate)
        {
            return new StageDefinition { Id = id, TargetScore = score, RewardCoins = reward, SkipRewardCoins = skip, CashoutRate = rate };
        }

        private static ZoneDefinition Zone(uint id, string name, int kind, float x, double scale)
        {
            return new ZoneDefinition
            {
                Id = id, Name = name, Kind = kind, CenterX = x, ScoreMultiplier = scale,
                RushEnabled = kind != 2, Priority = kind == 2 ? 0 : 100
            };
        }
    }
}
