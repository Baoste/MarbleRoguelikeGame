using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    [Preserve]
    public sealed class DeviceTransientInfo
    {
        private readonly Func<SimulationContext> context;
        private readonly int id;
        private readonly Vector3 position, velocity;
        private readonly float duration;
        internal DeviceTransientInfo(Func<SimulationContext> context, int id, Vector3 position, Vector3 velocity, float duration)
        { this.context = context; this.id = id; this.position = position; this.velocity = velocity; this.duration = duration; }
        public int Id { get { _ = context(); return id; } }
        public Vector3 Position { get { _ = context(); return position; } }
        public Vector3 Velocity { get { _ = context(); return velocity; } }
        public float Duration { get { _ = context(); return duration; } }
    }

    internal sealed class DeviceTransientSystem
    {
        internal sealed class Item
        {
            internal int Id, Source, Round;
            internal Vector3 Origin, Velocity;
            internal double StartedAt;
            internal float Duration;
            internal string Callback;
        }
        private readonly Dictionary<int, Item> items = new Dictionary<int, Item>();
        private int nextId;
        internal int Count => items.Count;
        internal int Spawn(SimulationContext c, int source, string path, Vector3 position, Vector3 velocity, float seconds, string callback)
        {
            if (!c.IsRunning || !DeviceDurabilitySystem.CanRun(c, source) ||
                !(c.Physics is IDeviceTransientVisuals visuals) || string.IsNullOrEmpty(path) ||
                !SimulationContext.IsFinite(position) || !SimulationContext.IsFinite(velocity) ||
                !BallInteractionSystem.Finite(velocity.sqrMagnitude) || !BallInteractionSystem.Finite(seconds) || seconds <= 0 || seconds > 60 ||
                items.Count >= 256 || nextId == int.MaxValue || (callback != null && callback.Length > 64) ||
                !SimulationContext.IsFinite(position + velocity * seconds) ||
                (c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds)) return 0;
            int id = ++nextId;
            if (!visuals.TryCreateTransientVisual(source, id, path, position)) return 0;
            items.Add(id, new Item { Id = id, Source = source, Round = c.Round.RoundId, Origin = position, Velocity = velocity,
                StartedAt = c.Round.Time, Duration = seconds, Callback = callback });
            return id;
        }
        internal void Tick(SimulationContext c)
        {
            var ids = new List<int>(items.Keys); ids.Sort();
            foreach (int id in ids)
            {
                if (!items.TryGetValue(id, out var item)) continue;
                if (!c.IsRunning || item.Round != c.Round.RoundId || !DeviceDurabilitySystem.CanRun(c, item.Source) ||
                    !(c.Physics is IDeviceTransientVisuals visuals) ||
                    (c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds))
                { Remove(c, id); continue; }
                double elapsed = c.Round.Time - item.StartedAt;
                var position = item.Origin + item.Velocity * (float)Math.Min(item.Duration, Math.Max(0, elapsed));
                if (!visuals.TryMoveTransientVisual(id, position)) { Remove(c, id); continue; }
                if (elapsed + 1e-7 < item.Duration) continue;
                Remove(c, id); // Always remove before Lua; a missing/throwing callback cannot leak the visual.
                if (!string.IsNullOrEmpty(item.Callback)) DeviceScriptSystem.DeviceEvent(c, item.Source, item.Callback, item);
            }
        }
        private void Remove(SimulationContext c, int id)
        { items.Remove(id); if (c.Physics is IDeviceTransientVisuals visuals) visuals.RemoveTransientVisual(id); }
        internal void Clear(SimulationContext c) { foreach (int id in new List<int>(items.Keys)) Remove(c, id); }
        internal void RemoveSource(SimulationContext c, int source)
        { foreach (var item in new List<Item>(items.Values)) if (item.Source == source) Remove(c, item.Id); }
    }
}
