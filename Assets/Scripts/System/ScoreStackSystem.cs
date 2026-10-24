using System;

namespace MarblesECS
{
    /// <summary>One shared, expiring addition to participating score zones' multipliers.</summary>
    internal static class ScoreStackSystem
    {
        internal static double Bonus(SimulationContext c) => c.Round.ScoreStackCount * c.Tuning.ScoreStackIncrement;

        internal static void Tick(SimulationContext c)
        {
            var round = c.Round;
            if (round.ScoreStackCount == 0 || round.Time + 1e-7 < round.ScoreStackExpiresAt) return;
            round.ScoreStackCount = 0;
            round.ScoreStackExpiresAt = 0;
            c.Round = round;
        }

        // Called only after the terminal-contact resolver has accepted a live, unsettled ball.
        internal static double RegisterScore(SimulationContext c, MarbleContact contact)
        {
            if (!contact.ScoreStackEnabled || (contact.Kind != MarbleContactKind.Score &&
                contact.Kind != MarbleContactKind.RandomScore)) return contact.Multiplier;
            Tick(c);
            var round = c.Round;
            double previousBonus = round.ScoreStackCount * c.Tuning.ScoreStackIncrement;
            double seconds = Math.Max(c.Tuning.ScoreStackMinimumSeconds,
                c.Tuning.ScoreStackInitialSeconds / (1 + previousBonus));
            if (round.ScoreStackCount < long.MaxValue) round.ScoreStackCount++;
            round.ScoreStackExpiresAt = round.Time + seconds;
            c.Round = round;
            // Keep the existing maximum accepted by zone/settlement numeric validation.
            return Math.Min(1000, contact.Multiplier + Bonus(c));
        }
    }
}
