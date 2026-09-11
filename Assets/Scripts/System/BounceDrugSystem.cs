using System;

namespace MarblesECS
{
    internal static class BounceDrugSystem
    {
        internal static void Expire(SimulationContext context)
        {
            var doses = context.Manager.GetBuffer<ActiveBounceDrugData>(context.PlayerEntity);
            for (int i = doses.Length - 1; i >= 0; i--)
                if (doses[i].EndsAt <= context.Round.Time) doses.RemoveAt(i);
        }

        internal static float Restitution(SimulationContext context, out int count, out double remaining)
        {
            double factor = 1;
            count = 0;
            remaining = 0;
            var doses = context.Manager.GetBuffer<ActiveBounceDrugData>(context.PlayerEntity);
            for (int i = 0; i < doses.Length; i++)
            {
                var dose = doses[i];
                if (dose.EndsAt <= context.Round.Time) continue;
                factor *= dose.Multiplier;
                count++;
                remaining = Math.Max(remaining, dose.EndsAt - context.Round.Time);
            }
            // Clamp only after all factors; high/low doses commute regardless of use order.
            return (float)Math.Min(1, context.Tuning.BaseRestitution * factor);
        }

        internal static void Clear(SimulationContext context) =>
            context.Manager.GetBuffer<ActiveBounceDrugData>(context.PlayerEntity).Clear();
    }
}
