using System;
using Unity.Entities;

namespace MarblesECS
{
    internal struct RushTriggerVisit : IBufferElementData
    {
        public int Source;
        public long Tick;
    }

    /// <summary>One board-wide meter. Balls carry contributions until a terminal zone accepts them.</summary>
    internal static class RushSystem
    {
        internal const double InitialContribution = 2, DeviceContribution = 5;
        internal const double Threshold = 666, Duration = 6, Cooldown = 3, ValueBonus = 5, ExtraShotsPerSecond = 5;
        internal static bool Active(SimulationContext c) => c.IsRunning && c.Round.RushRemaining > 1e-7;
        internal static double BallValue(SimulationContext c, Entity ball) =>
            (AttributeRuntime.Enabled(c) ? AttributeRuntime.Get(c, ball, GameAttribute.BALL_BASE_VALUE)
                : c.Manager.GetComponentData<MarbleScore>(ball).BaseScore) + (Active(c) ? ValueBonus : 0);
        internal static double FireInterval(SimulationContext c, double baseInterval) => Active(c)
            ? 1 / (1 / baseInterval + ExtraShotsPerSecond) : baseInterval;
        internal static double ShotCost(SimulationContext c, double cost) => Active(c) ? 0 : cost;
        internal static void Tick(SimulationContext c)
        {
            var round = c.Round;
            if (round.RushRemaining > 0)
            {
                double afterRush = c.StepSeconds - round.RushRemaining;
                round.RushRemaining = Math.Max(0, -afterRush);
                // Consume only the part of this step after expiry from the cooldown.
                if (round.RushRemaining < 1e-7)
                    round.RushCooldownRemaining = Math.Max(0, Cooldown - Math.Max(0, afterRush));
            }
            else round.RushCooldownRemaining = Math.Max(0, round.RushCooldownRemaining - c.StepSeconds);
            if (round.RushRemaining < 1e-7) round.RushRemaining = 0;
            if (round.RushCooldownRemaining < 1e-7) round.RushCooldownRemaining = 0;
            if (round.RushRemaining == 0) round.RushDuration = 0;
            c.Round = round;
        }
        internal static void AddContribution(SimulationContext c, Entity ball, double amount)
        {
            if (!MarbleRules.IsFinite(amount) || amount < 0 || amount > 1000000) throw new ArgumentOutOfRangeException(nameof(amount));
            var rush = c.Manager.GetComponentData<MarbleRush>(ball);
            rush.Contribution = Math.Min(1000000, rush.Contribution + amount);
            c.Manager.SetComponentData(ball, rush);
        }
        internal static void RecordTrigger(SimulationContext c, Entity ball, int source)
        {
            if (!c.Manager.Exists(ball) || !c.Manager.HasComponent<MarbleRush>(ball)) return;
            var visits = c.Manager.GetBuffer<RushTriggerVisit>(ball);
            for (int i = 0; i < visits.Length; i++)
            {
                var visit = visits[i];
                if (visit.Source != source) continue;
                if (visit.Tick == c.Round.Tick) return;
                visit.Tick = c.Round.Tick; visits[i] = visit; AddContribution(c, ball, DeviceContribution); return;
            }
            visits.Add(new RushTriggerVisit { Source = source, Tick = c.Round.Tick });
            AddContribution(c, ball, DeviceContribution);
        }
        internal static void RecordSpawn(SimulationContext c, Entity parent, System.Collections.Generic.IEnumerable<Entity> children, int source, bool accepted)
        {
            if (!accepted) return;
            double before = c.Manager.GetComponentData<MarbleRush>(parent).Contribution;
            RecordTrigger(c, parent, source);
            double delta = c.Manager.GetComponentData<MarbleRush>(parent).Contribution - before;
            foreach (var child in children)
            {
                AddContribution(c, child, delta);
                c.Manager.GetBuffer<RushTriggerVisit>(child).Add(new RushTriggerVisit { Source = source, Tick = c.Round.Tick });
            }
        }
        internal static void Settle(SimulationContext c, Entity ball, MarbleContact contact)
        {
            if (Active(c) || c.Round.RushCooldownRemaining > 0 ||
                (contact.Kind != MarbleContactKind.Drain && contact.RushDisabled)) return;
            var rush = c.Manager.GetComponentData<MarbleRush>(ball);
            double amount = contact.Kind == MarbleContactKind.Drain ? rush.Contribution
                : rush.HasScoreContribution ? rush.ScoreContribution : rush.Contribution * 1.5;
            var round = c.Round;
            round.RushMeter = Math.Max(0, Math.Min(Threshold, round.RushMeter + amount));
            if (round.RushMeter >= Threshold)
            {
                round.RushMeter = 0;
                round.RushRemaining = Duration + rush.DurationBonus;
                round.RushDuration = round.RushRemaining;
                if (round.Phase == (byte)RoundPhase.Draining && !round.CashoutRequested)
                { round.Phase = (byte)RoundPhase.Playing; round.DrainStartedAt = 0; }
            }
            c.Round = round;
        }
    }
}
