using System;

namespace MarblesECS
{
    internal static class PlayerBloodSystem
    {
        /// <summary>Restore spendable blood up to capacity, returning the amount actually restored.</summary>
        internal static double Restore(SimulationContext c, double amount)
        {
            if (!MarbleRules.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!c.IsRunning || c.Round.SettlementComplete || amount == 0) return 0;
            var player = c.Player; float before = player.Blood;
            player.Blood = (float)Math.Min(c.Tuning.BloodCapacity, (double)before + amount);
            double restored = (double)player.Blood - before;
            if (restored <= 0) return 0;
            c.Player = player;
            var round = c.Round;
            if (round.Phase == (byte)RoundPhase.Draining && !round.CashoutRequested)
            {
                var cheapest = AttributeRuntime.Enabled(c) ? AttributeRuntime.Prepare(c, 0) : null;
                double cost = cheapest == null ? MarbleRules.Quote(c.Tuning, 0).Cost : cheapest.Quote.Cost * cheapest.Count;
                if (player.Blood >= cost)
                {
                    round.Phase = (byte)RoundPhase.Playing; round.DrainStartedAt = 0; c.Round = round;
                    // BeginDraining cleared input; restoring blood never creates an unsolicited shot.
                }
            }
            return restored;
        }
    }
}
