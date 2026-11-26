namespace MarblesECS
{
    public enum GameCurrency : byte { CompanyScrip, BiomassScrap }

    internal static class CampaignWalletSystem
    {
        internal const long MaxBalance = 9000000000000000L;

        internal static long Balance(SimulationContext context, GameCurrency currency) => currency switch
        {
            GameCurrency.CompanyScrip => context.Campaign.CompanyScripBalance,
            GameCurrency.BiomassScrap => context.Player.BiomassScrapBalance,
            _ => throw new System.ArgumentOutOfRangeException(nameof(currency))
        };

        internal static void Set(SimulationContext context, GameCurrency currency, long balance)
        {
            switch (currency)
            {
                case GameCurrency.CompanyScrip:
                    var campaign = context.Campaign;
                    campaign.CompanyScripBalance = balance;
                    context.Campaign = campaign;
                    break;
                case GameCurrency.BiomassScrap:
                    var player = context.Player;
                    player.BiomassScrapBalance = balance;
                    context.Player = player;
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(currency));
            }
        }

        internal static bool CanSpend(SimulationContext context, GameCurrency currency, long cost) =>
            cost >= 0 && Balance(context, currency) >= cost;

        internal static bool TrySpend(SimulationContext context, GameCurrency currency, long cost)
        {
            if (!CanSpend(context, currency, cost)) return false;
            Set(context, currency, Balance(context, currency) - cost);
            return true;
        }

        internal static long Credit(SimulationContext context, GameCurrency currency, long amount)
        {
            long before = Balance(context, currency);
            long after = MarbleRules.AddScore(before, amount, MaxBalance);
            Set(context, currency, after);
            return after - before;
        }
    }
}
