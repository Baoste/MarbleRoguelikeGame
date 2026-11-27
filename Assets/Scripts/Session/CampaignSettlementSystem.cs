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
            if (round.Score < round.TargetScore)
                round.Phase = (byte)RoundPhase.Lost;
            else
            {
                round.Phase = (byte)(campaign.RepeatRounds ? RoundPhase.Build : RoundPhase.Won);
            }
            context.Campaign = campaign;
            context.Round = round;
        }
    }
}
