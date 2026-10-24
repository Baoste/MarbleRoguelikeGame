using System;

namespace MarblesECS
{
    /// <summary>One shared pool. All clocks and draws belong to the simulation, never the UI.</summary>
    internal static class GamblingWheelSystem
    {
        internal static void Deposit(SimulationContext context, double value)
        {
            if (!context.IsRunning || !MarbleRules.IsFinite(value) || value < 0) return;
            // Expire the previous pool before accepting a ball on the deadline.
            Tick(context);
            var round = context.Round;
            double total = Math.Min(context.Tuning.MaxRoundScore,
                round.PendingScore + round.FractionalPendingScore + value);
            round.PendingScore = (long)Math.Floor(total);
            round.FractionalPendingScore = total - round.PendingScore;
            round.WheelBallCount = Math.Min(int.MaxValue - 1, round.WheelBallCount) + 1;
            round.WheelWindowSeconds = GamblingWheelRules.WindowSeconds(round.WheelBallCount);
            round.WheelExpiresAt = round.Time + round.WheelWindowSeconds;
            context.Round = round;
        }

        internal static void Tick(SimulationContext context)
        {
            var round = context.Round;
            if (context.IsRunning && round.WheelBallCount > 0 && round.Time >= round.WheelExpiresAt)
                Draw(context);
        }

        internal static bool Draw(SimulationContext context)
        {
            var round = context.Round;
            // A zero-value ball is still an entry; an empty pool consumes no randomness.
            if (round.WheelBallCount == 0) return false;
            round.WheelLastPool = round.PendingScore + round.FractionalPendingScore;
            round.WheelLastMultiplier = GamblingWheelRules.Outcome(MarbleRules.NextRandom(ref round.RandomState));
            double payout = round.WheelLastPool * round.WheelLastMultiplier;
            double oldScore = round.Score + round.FractionalScore;
            double newScore = Math.Max(0, Math.Min(context.Tuning.MaxRoundScore, oldScore + payout));
            long previous = round.Score;
            round.Score = (long)Math.Floor(newScore);
            round.FractionalScore = newScore - round.Score;
            round.WheelLastScoreChange = round.Score - previous;
            round.WheelDrawCount++;
            round.PendingScore = 0;
            round.FractionalPendingScore = 0;
            round.WheelBallCount = 0;
            round.WheelExpiresAt = round.WheelWindowSeconds = 0;
            context.Round = round;
            return true;
        }
    }
}
