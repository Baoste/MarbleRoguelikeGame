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
                BaseValue = amount, DeviceMultiplier = 1, PermanentMultiplier = 1,
                ModifierMultiplier = 1, ZoneMultiplier = 1, LauncherMultiplier = 1, RushMultiplier = 1,
                Score = score, RoundScore = round.Score, PendingScore = round.PendingScore });
            return true;
        }
        internal static void Settle(SimulationContext c, Entity ball, MarbleContact contact)
        {
            BallInteractionSystem.Consume(c, ball, true);
            if (contact.Kind == MarbleContactKind.Drain) { RushSystem.Settle(c, ball, contact); return; }
            double zoneMultiplier = ScoreStackSystem.RegisterScore(c, contact);
            var round = c.Round;
            var scoreData = c.Manager.GetComponentData<MarbleScore>(ball);
            var modifiers = MarbleModifierSystem.Aggregate(c, ball);
            double value = Math.Max(0, RushSystem.BallValue(c, ball) + scoreData.ScoreBonus + modifiers.ScoreAdd);
            double speed = contact.Speed > 0 ? contact.Speed : c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity.magnitude;
            value *= 1 + AttributeRuntime.Get(c, ball, GameAttribute.SCORE_BY_SPEED) * Math.Min(1, speed / AttributeRuntime.Get(c, ball, GameAttribute.BALL_MAX_SPEED));
            double machine = Math.Min(c.Content.Definition(GameAttribute.MACHINE_MULT).MaxValue,
                AttributeRuntime.Get(c, ball, GameAttribute.MACHINE_MULT) * c.BoardScores.Multiplier(c, scoreData) * zoneMultiplier);
            double global = AttributeRuntime.Global(c, GameAttribute.GLOBAL_MULT);
            double rush = 1; // Rush now grants a temporary value bonus instead of a global score factor.
            global = Math.Min(c.Content.Definition(GameAttribute.GLOBAL_MULT).MaxValue,
                Math.Max(global, rush) * modifiers.ScoreMul);
            double raw = Math.Min(c.Tuning.MaxPerMarbleScore, (value * machine + contact.FlatScore) * global);
            bool pending = contact.Kind == MarbleContactKind.RandomScore;
            long score;
            if (pending)
            {
                GamblingWheelSystem.Deposit(c, raw);
                round = c.Round;
                score = MarbleRules.RoundScore(raw, c.Tuning.MaxPerMarbleScore);
            }
            else
            {
                double total = raw + round.FractionalScore;
                score = MarbleRules.RoundScore(total, c.Tuning.MaxPerMarbleScore);
                round.FractionalScore = total - Math.Floor(total);
                round.Score = MarbleRules.AddScore(round.Score, score, c.Tuning.MaxRoundScore);
            }
            c.Round = round;
            RushSystem.Settle(c, ball, contact);
            c.ScoreEvents.Enqueue(new MarbleScoreEvent { Key = contact.Key, ZoneId = contact.TargetId, Position = contact.Position,
                BaseValue = value, DeviceMultiplier = machine, PermanentMultiplier = machine, ZoneMultiplier = zoneMultiplier,
                ScoreStackBonus = zoneMultiplier - contact.Multiplier, FlatScore = contact.FlatScore,
                ModifierMultiplier = modifiers.ScoreMul, RushMultiplier = rush,
                Score = score, RoundScore = round.Score, IsPending = pending, PendingScore = round.PendingScore });
        }
    }
}
