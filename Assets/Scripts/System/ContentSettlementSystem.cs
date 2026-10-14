using System;
using Unity.Entities;

namespace MarblesECS
{
    internal static class ContentSettlementSystem
    {
        // Lua chooses the score formula. The engine owns one-shot settlement and score event accounting.
        internal static bool TrySettleAmount(SimulationContext c, Entity ball, MarbleContact contact, double amount)
        {
            if (!MarbleRules.IsFinite(amount) || amount < 0 || !c.Manager.Exists(ball) ||
                c.Manager.GetComponentData<SettlementState>(ball).IsSettled ||
                c.Manager.GetComponentData<DespawnState>(ball).PendingDespawn) return false;
            BallInteractionSystem.Consume(c, ball, true);
            c.Manager.SetComponentData(ball, new DespawnState { PendingDespawn = true, Reason = MarbleDespawnReason.Scored });
            var round = c.Round;
            double total = Math.Min(c.Tuning.MaxPerMarbleScore, amount) + round.FractionalScore;
            long score = MarbleRules.RoundScore(total, c.Tuning.MaxPerMarbleScore);
            round.FractionalScore = total - Math.Floor(total);
            round.Score = MarbleRules.AddScore(round.Score, score, c.Tuning.MaxRoundScore);
            c.Round = round;
            c.ScoreEvents.Enqueue(new MarbleScoreEvent { Key = contact.Key, ZoneId = contact.TargetId, Position = contact.Position,
                BaseValue = amount, DrugMultiplier = 1, DeviceMultiplier = 1, PermanentMultiplier = 1,
                ModifierMultiplier = 1, ZoneMultiplier = 1, LauncherMultiplier = 1, RushMultiplier = 1,
                Score = score, RoundScore = round.Score, PendingScore = round.PendingScore });
            return true;
        }
        internal static void Settle(SimulationContext c, Entity ball, MarbleContact contact)
        {
            BallInteractionSystem.Consume(c, ball, true);
            if (contact.Kind == MarbleContactKind.Drain) { RushSystem.Settle(c, ball, contact); return; }
            var round = c.Round;
            var scoreData = c.Manager.GetComponentData<MarbleScore>(ball);
            var features = c.Manager.GetComponentData<BallFeatures>(ball);
            var modifiers = MarbleModifierSystem.Aggregate(c, ball);
            double value = Math.Max(0, RushSystem.BallValue(c, ball) + scoreData.ScoreBonus + modifiers.ScoreAdd);
            double speed = contact.Speed > 0 ? contact.Speed : c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity.magnitude;
            value *= 1 + AttributeRuntime.Get(c, ball, GameAttribute.SCORE_BY_SPEED) * Math.Min(1, speed / AttributeRuntime.Get(c, ball, GameAttribute.BALL_MAX_SPEED));
            if (round.RushRemaining > 0) value *= features.RushScoreMultiplier;
            double machine = Math.Min(c.Content.Definition(GameAttribute.MACHINE_MULT).MaxValue,
                AttributeRuntime.Get(c, ball, GameAttribute.MACHINE_MULT) * c.BoardScores.Multiplier(c, scoreData) * contact.Multiplier);
            double global = AttributeRuntime.Global(c, GameAttribute.GLOBAL_MULT);
            double rush = 1; // Rush now grants a temporary value bonus instead of a global score factor.
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
            c.Round = round;
            RushSystem.Settle(c, ball, contact);
            c.ScoreEvents.Enqueue(new MarbleScoreEvent { Key = contact.Key, ZoneId = contact.TargetId, Position = contact.Position,
                BaseValue = value, DeviceMultiplier = machine, PermanentMultiplier = machine, ZoneMultiplier = contact.Multiplier,
                DrugMultiplier = c.Player.DrugScoreMultiplier, ModifierMultiplier = modifiers.ScoreMul, RushMultiplier = rush,
                Score = score, RoundScore = round.Score, IsPending = pending, PendingScore = round.PendingScore });
        }
    }
}
