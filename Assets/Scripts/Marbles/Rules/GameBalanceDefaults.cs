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
                    BaseScore = 10, TargetScore = 350,
                    FireIntervalSeconds = 0.15f, LaunchSpeed = 5, RushExtendsDuration = false
                },
                Campaign = new CampaignTuning(),
                Devices = System.Array.Empty<DeviceDefinition>()
            };
        }

    }
}
