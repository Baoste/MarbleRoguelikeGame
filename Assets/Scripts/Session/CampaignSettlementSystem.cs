using System;

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
            campaign.LastSkippedStages = 0;
            if (campaign.TotalScore < round.TargetScore)
                round.Phase = (byte)RoundPhase.Lost;
            else
                AdvanceStages(context, ref campaign, ref round);
            context.Campaign = campaign;
            context.Round = round;
            if (round.Phase == (byte)RoundPhase.Shop) CampaignShopSystem.Generate(context);
        }

        private static void AdvanceStages(SimulationContext context, ref CampaignData campaign, ref RoundData round)
        {
            var stages = context.Balance.Stages;
            int first = campaign.StageIndex;
            int next = first;
            while (next < stages.Length && campaign.TotalScore >= stages[next].TargetScore)
            {
                var stage = stages[next];
                long reward = stage.RewardCoins;
                if (next > first)
                    reward = MarbleRules.AddScore(reward, stage.SkipRewardCoins, CampaignStateUtility.MaxCoins);
                campaign.LastRewardCoins = MarbleRules.AddScore(campaign.LastRewardCoins, reward, CampaignStateUtility.MaxCoins);
                next++;
            }
            campaign.LastSkippedStages = Math.Max(0, next - first - 1);
            campaign.StagesCleared = next;
            if (AttributeRuntime.Enabled(context))
            {
                long oldTotal = campaign.TotalScore - round.Score;
                long excess = Math.Max(0, campaign.TotalScore - Math.Max(oldTotal, round.TargetScore));
                long overkill = MarbleRules.RoundScore(excess * context.Content.OverkillCoinsPerScore *
                    AttributeRuntime.Global(context, GameAttribute.SCORE_OVERKILL_RATE), CampaignStateUtility.MaxCoins);
                campaign.LastRewardCoins = MarbleRules.AddScore(campaign.LastRewardCoins, overkill, CampaignStateUtility.MaxCoins);
                campaign.LastRewardCoins = MarbleRules.RoundScore(campaign.LastRewardCoins * AttributeRuntime.Global(context, GameAttribute.COIN_GAIN_RATE), CampaignStateUtility.MaxCoins);
            }
            campaign.Coins = MarbleRules.AddScore(campaign.Coins, campaign.LastRewardCoins, CampaignStateUtility.MaxCoins);
            campaign.StageIndex = Math.Min(next, stages.Length - 1);
            round.Phase = (byte)(next == stages.Length ? RoundPhase.Won : RoundPhase.Shop);
        }
    }
}
