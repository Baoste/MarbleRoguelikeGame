using System;

namespace MarblesECS
{
    internal static class RoundFlowSystem
    {
        internal static void Execute(SimulationContext context)
        {
            var round = context.Round;
            // Compare the cheapest possible shot, so reducing flow remains possible when a large shot is unaffordable.
            var cheapest = AttributeRuntime.Enabled(context) ? AttributeRuntime.Prepare(context, 0) : null;
            if (round.Phase == (byte)RoundPhase.Playing && !RushSystem.Active(context) && context.Player.Blood <
                (cheapest == null ? MarbleRules.Quote(context.Tuning, 0).Cost : cheapest.Quote.Cost * cheapest.Count))
            {
                context.BeginDraining();
                round = context.Round;
            }
            if (round.Phase != (byte)RoundPhase.Draining || round.ActiveMarbleCount != 0) return;
            GamblingWheelSystem.Draw(context);
            round = context.Round;
            if (context.Balance != null)
            {
                CampaignSettlementSystem.Execute(context);
                round = context.Round;
            }
            else round.Phase = (byte)(round.Score >= round.TargetScore ? RoundPhase.Won : RoundPhase.Lost);
            round.RushEndTick = 0;
            round.RushRemaining = round.RushMeter = round.RushDuration = 0;
            round.ScoreStackCount = 0;
            round.ScoreStackExpiresAt = 0;
            round.ScoreStackWindowSeconds = 0;
            context.Round = round;
            context.Transients.Clear(context);
            context.BoardScores.Clear();
            context.BoardRush.Clear();
            context.ClearFireInput();
        }
    }
}
