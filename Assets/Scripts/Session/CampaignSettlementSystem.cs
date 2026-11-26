namespace MarblesECS
{
    internal static class CampaignSettlementSystem
    {
        internal static void Execute(SimulationContext context)
        {
            var round = context.Round;
            if (round.SettlementComplete) return;
            var campaign = context.Campaign;
            var rules = context.Balance.Campaign;

            GamblingWheelSystem.Draw(context);
            round = context.Round;
            round.SettlementComplete = true;
            campaign.TotalScore = MarbleRules.AddScore(campaign.TotalScore, round.Score, rules.MaxTotalScore);
            campaign.LastRewardCoins = 0;
            if (round.Score < round.TargetScore)
                round.Phase = (byte)RoundPhase.Lost;
            else
            {
                if (campaign.Production.Currency == GameCurrency.Coins)
                {
                    campaign.LastRewardCoins = Reward(context, campaign.RewardCoins, round.Score - round.TargetScore);
                    campaign.Coins = MarbleRules.AddScore(campaign.Coins, campaign.LastRewardCoins, CampaignStateUtility.MaxCoins);
                }
                round.Phase = (byte)(campaign.RepeatRounds ? RoundPhase.Build : RoundPhase.Won);
            }
            context.Campaign = campaign;
            context.Round = round;
        }

        private static long Reward(SimulationContext context, long reward, long excess)
        {
            if (!AttributeRuntime.Enabled(context)) return reward;
            long overkill = MarbleRules.RoundScore(excess * context.Content.OverkillCoinsPerScore *
                AttributeRuntime.Global(context, GameAttribute.SCORE_OVERKILL_RATE), CampaignStateUtility.MaxCoins);
            long combined = MarbleRules.AddScore(reward, overkill, CampaignStateUtility.MaxCoins);
            return MarbleRules.RoundScore(combined * AttributeRuntime.Global(context, GameAttribute.COIN_GAIN_RATE),
                CampaignStateUtility.MaxCoins);
        }
    }
}
