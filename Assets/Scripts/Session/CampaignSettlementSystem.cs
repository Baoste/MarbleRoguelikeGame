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

            if (!round.GamblingResolved)
            {
                // Every RandomScoreZone contributes to this one pool and this one draw.
                round.GamblingStake = round.PendingScore;
                round.GamblingWon = round.PendingScore > 0 &&
                    MarbleRules.Roll(ref round.RandomState, rules.GamblingWinChance);
                round.GamblingPayout = round.GamblingWon
                    ? MarbleRules.RoundScore(round.PendingScore * rules.GamblingWinMultiplier,
                        context.Tuning.MaxRoundScore)
                    : 0;
                round.PendingScore = 0;
                round.GamblingResolved = true;
                round.GamblingRevealComplete = round.GamblingStake == 0;
                context.Round = round;
            }

            // A non-empty draw waits for the presentation reveal before committing scores.
            if (!round.GamblingRevealComplete) return;

            round.Score = MarbleRules.AddScore(round.Score, round.GamblingPayout, context.Tuning.MaxRoundScore);
            round.SettlementComplete = true;
            campaign.TotalScore = MarbleRules.AddScore(campaign.TotalScore, round.Score, rules.MaxTotalScore);
            campaign.RandomState = round.RandomState;
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

        internal static bool CompleteReveal(SimulationContext context)
        {
            var round = context.Round;
            if (!round.GamblingResolved || round.GamblingRevealComplete || round.SettlementComplete)
                return false;
            round.GamblingRevealComplete = true;
            context.Round = round;
            Execute(context);
            return true;
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
            campaign.Coins = MarbleRules.AddScore(campaign.Coins, campaign.LastRewardCoins, CampaignStateUtility.MaxCoins);
            campaign.StageIndex = Math.Min(next, stages.Length - 1);
            round.Phase = (byte)(next == stages.Length ? RoundPhase.Won : RoundPhase.Shop);
        }
    }
}
