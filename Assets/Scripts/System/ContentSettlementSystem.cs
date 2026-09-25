using System;
using Unity.Entities;

namespace MarblesECS
{
    internal static class ContentSettlementSystem
    {
        internal static void Settle(SimulationContext c, Entity ball, MarbleContact contact)
        {
            DeviceEffectSystem.Consume(c, ball, true);
            if (contact.Kind == MarbleContactKind.Drain) return;
            var round = c.Round;
            var scoreData = c.Manager.GetComponentData<MarbleScore>(ball);
            var features = c.Manager.GetComponentData<BallFeatures>(ball);
            var modifiers = MarbleModifierSystem.Aggregate(c, ball);
            double value = Math.Max(0, AttributeRuntime.Get(c, ball, GameAttribute.BALL_BASE_VALUE) + scoreData.ScoreBonus + modifiers.ScoreAdd);
            double speed = contact.Speed > 0 ? contact.Speed : c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity.magnitude;
            value *= 1 + AttributeRuntime.Get(c, ball, GameAttribute.SCORE_BY_SPEED) * Math.Min(1, speed / AttributeRuntime.Get(c, ball, GameAttribute.BALL_MAX_SPEED));
            if (round.RushRemaining > 0) value *= features.RushScoreMultiplier;
            double machine = Math.Min(c.Content.Definition(GameAttribute.MACHINE_MULT).MaxValue,
                AttributeRuntime.Get(c, ball, GameAttribute.MACHINE_MULT) * scoreData.ScoreMultiplier * contact.Multiplier);
            double global = AttributeRuntime.Global(c, GameAttribute.GLOBAL_MULT);
            double rush = round.RushRemaining > 0 ? AttributeRuntime.Global(c, GameAttribute.RUSH_MULT) : 1;
            global = Math.Min(c.Content.Definition(GameAttribute.GLOBAL_MULT).MaxValue,
                Math.Max(global, rush) * modifiers.ScoreMul * c.Player.DrugScoreMultiplier);
            double raw = Math.Min(c.Tuning.MaxPerMarbleScore, (value * machine + contact.FlatScore) * global);
            bool pending = contact.Kind == MarbleContactKind.RandomScore;
            if (pending) raw *= Math.Min(c.Content.Definition(GameAttribute.GAMBLE_POT_VALUE).MaxValue,
                AttributeRuntime.Get(c, ball, GameAttribute.BALL_GAMBLE_VALUE_MOD) * AttributeRuntime.Get(c, ball, GameAttribute.GAMBLE_POT_VALUE));
            raw = Math.Min(c.Tuning.MaxPerMarbleScore, raw);
            double total = raw + (pending ? round.FractionalPendingScore : round.FractionalScore);
            long score = MarbleRules.RoundScore(total, c.Tuning.MaxPerMarbleScore);
            if (pending)
            { round.FractionalPendingScore = total - Math.Floor(total); round.PendingScore = MarbleRules.AddScore(round.PendingScore, score, c.Tuning.MaxRoundScore); }
            else
            { round.FractionalScore = total - Math.Floor(total); round.Score = MarbleRules.AddScore(round.Score, score, c.Tuning.MaxRoundScore); }
            if (!contact.RushDisabled)
            {
                bool canRoll = !features.Clover || c.CloverFamilies.Add(features.Family);
                var chance = c.Manager.GetComponentData<MarbleRush>(ball);
                double probability = Math.Min(1, Math.Max(0, (features.Clover ? .75 : contact.BaseRushChance) + features.RushChanceAdd + chance.RushChanceBonus + modifiers.RushChanceAdd));
                bool triggered = canRoll && MarbleRules.Roll(ref round.RandomState, probability);
                round.RushMeter += AttributeRuntime.Get(c, ball, GameAttribute.BALL_RUSH_GAIN);
                if (round.RushMeter >= c.Content.RushThreshold)
                { triggered = true; round.RushMeter %= c.Content.RushThreshold; }
                if (triggered) round.RushRemaining = Math.Min(c.Content.Definition(GameAttribute.RUSH_DURATION).MaxValue,
                    Math.Max(round.RushRemaining, AttributeRuntime.Get(c, ball, GameAttribute.RUSH_DURATION)));
            }
            c.Round = round;
            c.ScoreEvents.Enqueue(new MarbleScoreEvent { Key = contact.Key, ZoneId = contact.TargetId, Position = contact.Position,
                BaseValue = value, DeviceMultiplier = machine, PermanentMultiplier = machine, ZoneMultiplier = contact.Multiplier,
                DrugMultiplier = c.Player.DrugScoreMultiplier, ModifierMultiplier = modifiers.ScoreMul, RushMultiplier = rush,
                Score = score, RoundScore = round.Score, IsPending = pending, PendingScore = round.PendingScore });
        }
    }
}
