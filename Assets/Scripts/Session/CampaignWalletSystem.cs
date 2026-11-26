using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    public enum GameCurrency : byte { CompanyScrip, BiomassScrap, Coins }
    internal struct DayScripReward : IBufferElementData { internal FixedString128Bytes DayId; }

    internal static class CampaignWalletSystem
    {
        internal const long MaxBalance = 9000000000000000L;

        internal static long Balance(SimulationContext context, GameCurrency currency) => currency switch
        {
            GameCurrency.CompanyScrip => context.Campaign.CompanyScripBalance,
            GameCurrency.BiomassScrap => context.Player.BiomassScrapBalance,
            GameCurrency.Coins => context.Campaign.Coins,
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
                case GameCurrency.Coins:
                    var session = context.Campaign;
                    session.Coins = balance;
                    context.Campaign = session;
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

        internal static void AwardClearScrip(SimulationContext context, FixedString128Bytes dayId, long amount)
        {
            var paid = context.Manager.GetBuffer<DayScripReward>(context.RoundEntity);
            foreach (var reward in paid)
                if (reward.DayId.Equals(dayId)) return;
            paid.Add(new DayScripReward { DayId = dayId });
            long credited = Credit(context, GameCurrency.CompanyScrip, amount);
            var campaign = context.Campaign;
            campaign.LastCompanyScripReward = credited;
            context.Campaign = campaign;
        }
    }
}
