using Unity.Entities;

namespace MarblesECS
{
    internal static class SettlementSystem
    {
        internal static void Settle(SimulationContext context, Entity entity, MarbleContact contact)
        {
            if (AttributeRuntime.Enabled(context)) { ContentSettlementSystem.Settle(context, entity, contact); return; }
            var manager = context.Manager;
            // Close the reward window before changing shared player resources.
            manager.SetComponentData(entity, new SettlementState { IsSettled = true });
            manager.SetComponentData(entity, new DespawnState { PendingDespawn = true,
                Reason = contact.Kind == MarbleContactKind.Drain ? MarbleDespawnReason.Drained : MarbleDespawnReason.Scored });
            if (contact.Kind == MarbleContactKind.Drain) { RushSystem.Settle(context, entity, contact); return; }

            double zoneMultiplier = ScoreStackSystem.RegisterScore(context, contact);
            var round = context.Round;
            var marble = manager.GetComponentData<MarbleScore>(entity);
            var modifiers = MarbleModifierSystem.Aggregate(context, entity);
            double rush = 1d;
            long score = MarbleRules.Settle(RushSystem.BallValue(context, entity), marble.ScoreBonus, modifiers.ScoreAdd,
                marble.ScoreMultiplier, modifiers.ScoreMul,
                zoneMultiplier, contact.FlatScore, rush, context.Tuning.MaxPerMarbleScore);
            bool isPending = contact.Kind == MarbleContactKind.RandomScore;
            if (isPending) { GamblingWheelSystem.Deposit(context, score); round = context.Round; }
            else round.Score = MarbleRules.AddScore(round.Score, score, context.Tuning.MaxRoundScore);

            context.Round = round;
            RushSystem.Settle(context, entity, contact);
            context.ScoreEvents.Enqueue(new MarbleScoreEvent
            {
                Key = contact.Key, ZoneId = contact.TargetId, Position = contact.Position,
                BaseValue = marble.BaseScore, ScoreBonus = marble.ScoreBonus, ScoreAdd = modifiers.ScoreAdd,
                FlatScore = contact.FlatScore,
                PermanentMultiplier = marble.ScoreMultiplier, ModifierMultiplier = modifiers.ScoreMul,
                DeviceMultiplier = marble.ScoreMultiplier, ZoneMultiplier = zoneMultiplier,
                ScoreStackBonus = zoneMultiplier - contact.Multiplier,
                LauncherMultiplier = 1, RushMultiplier = rush, Score = score, RoundScore = round.Score,
                IsPending = isPending, PendingScore = round.PendingScore
            });
        }

    }
}
