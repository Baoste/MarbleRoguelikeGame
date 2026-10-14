using System;
using System.Collections.Generic;
using Unity.Entities;

namespace MarblesECS
{
    /// <summary>Named, source-owned probability offsets evaluated by scoring zones; never baked into marbles.</summary>
    internal sealed class BoardRushEffectSystem
    {
        private sealed class Effect { internal int Source, Round; internal string Key; internal double Amount, End; }
        private readonly List<Effect> effects = new List<Effect>();
        internal void Clear() => effects.Clear();
        private static bool Active(SimulationContext c, Effect e) => c.IsRunning && e.Round == c.Round.RoundId &&
            c.Round.Time + 1e-7 < e.End && CampaignStateUtility.FindDevice(c, e.Source, out var entity) &&
            c.Manager.GetComponentData<OwnedDeviceData>(entity).Placed && c.Physics is IMarblePhysicsCommands commands &&
            commands.TryGetDevicePose(e.Source, out _);
        private void Prune(SimulationContext c) => effects.RemoveAll(e => !Active(c, e));
        internal bool Set(SimulationContext c, int source, string key, double amount, double seconds)
        {
            DeviceRoundState.ValidateKey(key);
            if (!MarbleRules.IsFinite(amount) || amount < 0 || amount > 1 ||
                !MarbleRules.IsFinite(seconds) || seconds <= 0 || seconds > 60) throw new ArgumentOutOfRangeException();
            Prune(c);
            if (!c.IsRunning || !DeviceDurabilitySystem.CanRun(c, source) || !(c.Physics is IMarblePhysicsCommands commands) ||
                !commands.TryGetDevicePose(source, out _)) return false;
            var effect = effects.Find(e => e.Source == source && e.Key == key);
            if (effect == null)
            {
                if (effects.Count >= 256) return false;
                effect = new Effect { Source = source, Key = key, Round = c.Round.RoundId }; effects.Add(effect);
            }
            effect.Amount = amount; effect.End = c.Round.Time + seconds; return true;
        }
        internal bool Remove(SimulationContext c, int source, string key)
        {
            DeviceRoundState.ValidateKey(key); Prune(c);
            return effects.RemoveAll(e => e.Source == source && e.Key == key) != 0;
        }
        internal double EndTime(SimulationContext c, int source, string key)
        {
            DeviceRoundState.ValidateKey(key); Prune(c);
            return effects.Find(e => e.Source == source && e.Key == key)?.End ?? 0;
        }
        internal double Bonus(SimulationContext c)
        { Prune(c); double sum = 0; foreach (var effect in effects) sum += effect.Amount; return Math.Min(1, sum); }
    }
}
