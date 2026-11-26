using System;
using System.Collections.Generic;
using Unity.Entities;

namespace MarblesECS
{
    /// <summary>Timed board factors evaluated against each ball's multiplier when read.</summary>
    internal sealed class BoardScoreEffectSystem
    {
        private sealed class Effect { internal int Id, Source, Round; internal double End, Factor; }
        private readonly List<Effect> effects = new List<Effect>();
        private int nextId;
        internal void Clear() => effects.Clear();
        private static bool Active(SimulationContext c, Effect e) => c.IsRunning && e.Round == c.Round.RoundId &&
            c.Round.Time + 1e-7 < e.End && CampaignStateUtility.FindDevice(c, e.Source, out var entity) &&
            c.Manager.GetComponentData<OwnedDeviceData>(entity).Placed;
        private void Prune(SimulationContext c) => effects.RemoveAll(e => !Active(c, e));
        internal int Add(SimulationContext c, int source, double amount, double seconds)
        {
            if (!MarbleRules.IsFinite(amount) || amount < 0 || amount > 1000 ||
                !MarbleRules.IsFinite(seconds) || seconds <= 0 || seconds > 60) throw new ArgumentOutOfRangeException();
            Prune(c);
            if (!c.IsRunning || !DeviceDurabilitySystem.CanRun(c, source) || effects.Count >= 256) return 0;
            var effect = new Effect { Id = checked(++nextId), Source = source, Round = c.Round.RoundId, End = c.Round.Time + seconds, Factor = amount };
            effects.Add(effect); Record(c, effect); return effect.Id;
        }
        internal double EndTime(SimulationContext c, int id)
        {
            Prune(c);
            foreach (var effect in effects) if (effect.Id == id) return effect.End;
            return 0;
        }
        internal void Tick(SimulationContext c)
        { Prune(c); foreach (var effect in effects) Record(c, effect); }
        private static void Record(SimulationContext c, Effect effect)
        {
            if (effect.Factor == 1) return;
            foreach (var pair in c.Marbles)
                if (c.TryGetFlying(pair.Key, out var ball)) BallInteractionSystem.RecordDeviceEffect(c, ball, effect.Source);
        }
        private ScoreMultiplierProduct WithBoardFactors(SimulationContext context, ScoreMultiplierProduct product)
        {
            Prune(context);
            foreach (var effect in effects) product.Multiply(effect.Factor);
            return product;
        }

        internal double DeviceMultiplier(SimulationContext c, MarbleScore score) => WithBoardFactors(c, score.DeviceMultiplier).Value;
        internal double Multiplier(SimulationContext c, MarbleScore score)
        {
            var product = WithBoardFactors(c, score.DeviceMultiplier);
            product.Multiply(score.BaseMultiplier);
            return product.Value;
        }
    }
}
