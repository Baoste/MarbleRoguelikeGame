using System;

namespace MarblesECS
{
    internal static class RoundFlowSystem
    {
        internal static void Execute(SimulationContext context)
        {
            var round = context.Round;
            // Compare the cheapest possible shot, so reducing flow remains possible when a large shot is unaffordable.
            if (round.Phase == (byte)RoundPhase.Playing && context.Player.Blood < MarbleRules.Quote(context.Tuning, 0).Cost)
            {
                context.BeginDraining();
                round = context.Round;
            }
            if (round.Phase != (byte)RoundPhase.Draining || round.ActiveMarbleCount != 0) return;
            if (context.Balance != null)
            {
                CampaignSettlementSystem.Execute(context);
                round = context.Round;
            }
            else round.Phase = (byte)(round.Score >= round.TargetScore ? RoundPhase.Won : RoundPhase.Lost);
            round.RushEndTick = 0;
            context.Round = round;
            context.ClearFireInput();
            context.Manager.GetBuffer<ActiveScoreEffectData>(context.PlayerEntity).Clear();
            BounceDrugSystem.Clear(context);
            var player = context.Player;
            player.DrugScoreMultiplier = 1;
            context.Player = player;
        }
    }
}
