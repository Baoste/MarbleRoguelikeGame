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
        internal const double Threshold = 100, Duration = 10, ValueBonus = 5, ExtraShotsPerSecond = 5;
        internal static bool Active(SimulationContext c) => c.IsRunning && c.Round.RushRemaining > 1e-7;
        internal static double BallValue(SimulationContext c, Entity ball) =>
            (AttributeRuntime.Enabled(c) ? AttributeRuntime.Get(c, ball, GameAttribute.BALL_BASE_VALUE)
                : c.Manager.GetComponentData<MarbleScore>(ball).BaseScore) + (Active(c) ? ValueBonus : 0);
        internal static double FireInterval(SimulationContext c) => Active(c)
            ? 1 / (1 / c.Tuning.FireIntervalSeconds + ExtraShotsPerSecond) : c.Tuning.FireIntervalSeconds;
        internal static double ShotCost(SimulationContext c, double cost) => Active(c) ? 0 : cost;
        internal static void Tick(SimulationContext c)
        {
            var round = c.Round;
            bool wasActive = round.RushRemaining > 1e-7;
            round.RushRemaining = Math.Max(0, round.RushRemaining - c.StepSeconds);
            if (round.RushRemaining < 1e-7) round.RushRemaining = 0;
            c.Round = round;
            if (wasActive && round.RushRemaining == 0)
            {
                var launcher = c.Launcher;
                double faster = 1 / (1 / c.Tuning.FireIntervalSeconds + ExtraShotsPerSecond);
                launcher.NextFireAt = Math.Max(round.Time, launcher.NextFireAt + c.Tuning.FireIntervalSeconds - faster);
                c.Launcher = launcher;
            }
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
            if (Active(c) || (contact.Kind != MarbleContactKind.Drain && contact.RushDisabled)) return;
            var rush = c.Manager.GetComponentData<MarbleRush>(ball);
            double amount = contact.Kind == MarbleContactKind.Drain ? rush.Contribution
                : rush.HasScoreContribution ? rush.ScoreContribution : rush.Contribution * 1.5;
            var round = c.Round;
            round.RushMeter = Math.Max(0, Math.Min(Threshold, round.RushMeter + amount));
            if (round.RushMeter >= Threshold)
            {
                round.RushMeter = 0;
                round.RushRemaining = Duration + rush.DurationBonus;
                if (round.Phase == (byte)RoundPhase.Draining && !round.CashoutRequested)
                { round.Phase = (byte)RoundPhase.Playing; round.DrainStartedAt = 0; }
                // Apply the faster cadence to an existing launcher cooldown as well.
                var launcher = c.Launcher;
                double interval = 1 / (1 / c.Tuning.FireIntervalSeconds + ExtraShotsPerSecond);
                launcher.NextFireAt = Math.Min(launcher.NextFireAt, round.Time + interval);
                c.Launcher = launcher;
            }
            c.Round = round;
        }
    }
}
