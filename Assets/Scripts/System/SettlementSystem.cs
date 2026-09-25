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
            if (contact.Kind == MarbleContactKind.Drain) return;

            var round = context.Round;
            var marble = manager.GetComponentData<MarbleScore>(entity);
            var chance = manager.GetComponentData<MarbleRush>(entity);
            var modifiers = MarbleModifierSystem.Aggregate(context, entity);
            double drug = context.Player.DrugScoreMultiplier;
            double rush = round.Tick < round.RushEndTick ? 2d : 1d;
            long score = MarbleRules.Settle(marble.BaseScore, marble.ScoreBonus, modifiers.ScoreAdd,
                marble.ScoreMultiplier, MarbleRules.CombineDeviceMultiplier(modifiers.ScoreMul, drug),
                contact.Multiplier, contact.FlatScore, rush, context.Tuning.MaxPerMarbleScore);
            bool isPending = contact.Kind == MarbleContactKind.RandomScore && context.Balance != null;
            if (isPending) round.PendingScore = MarbleRules.AddScore(round.PendingScore, score, context.Tuning.MaxRoundScore);
            else round.Score = MarbleRules.AddScore(round.Score, score, context.Tuning.MaxRoundScore);

            double probability = MarbleRules.RushProbability(contact.BaseRushChance, chance.RushChanceBonus,
                modifiers.RushChanceAdd);
            if (!contact.RushDisabled && MarbleRules.Roll(ref round.RandomState, probability))
                ExtendRush(context, ref round);
            context.Round = round;
            context.ScoreEvents.Enqueue(new MarbleScoreEvent
            {
                Key = contact.Key, ZoneId = contact.TargetId, Position = contact.Position,
                BaseValue = marble.BaseScore, ScoreBonus = marble.ScoreBonus, ScoreAdd = modifiers.ScoreAdd,
                FlatScore = contact.FlatScore, DrugMultiplier = drug,
                PermanentMultiplier = marble.ScoreMultiplier, ModifierMultiplier = modifiers.ScoreMul,
                DeviceMultiplier = marble.ScoreMultiplier, ZoneMultiplier = contact.Multiplier,
                LauncherMultiplier = 1, RushMultiplier = rush, Score = score, RoundScore = round.Score,
                IsPending = isPending, PendingScore = round.PendingScore
            });
        }

        private static void ExtendRush(SimulationContext context, ref RoundData round)
        {
            double rushSecondsPerTick = context.StepSeconds * context.Tuning.RushTimeScale;
            long duration = (long)System.Math.Ceiling(context.Tuning.RushDurationSeconds / rushSecondsPerTick);
            long start = context.Tuning.RushExtendsDuration ? System.Math.Max(round.Tick, round.RushEndTick) : round.Tick;
            long maximum = checked(round.Tick + (long)System.Math.Ceiling(context.Tuning.MaxRushSeconds / rushSecondsPerTick));
            round.RushEndTick = System.Math.Min(maximum, checked(start + duration));
        }
    }
}
