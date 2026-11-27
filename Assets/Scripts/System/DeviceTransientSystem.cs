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
            internal string Callback, NodePath;

            internal Vector3 PositionAt(double time) =>
                Origin + Velocity * (float)Math.Min(Duration, Math.Max(0, time - StartedAt));
        }
        private readonly Dictionary<int, Item> items = new Dictionary<int, Item>();
        private readonly List<int> ticking = new List<int>();
        private readonly List<int> removing = new List<int>();
        private int nextId;
        internal int Count => items.Count;
        internal Dictionary<int, Item>.ValueCollection Items => items.Values;
        internal bool Contains(int id) => items.ContainsKey(id);
        internal int Spawn(SimulationContext c, int source, string path, Vector3 position, Vector3 velocity, float seconds, string callback)
        {
            if (!c.IsRunning || !DeviceDurabilitySystem.CanRun(c, source) ||
                (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(callback)) || (path != null && path.Length > 256) ||
                !SimulationContext.IsFinite(position) || !SimulationContext.IsFinite(velocity) ||
                !BallInteractionSystem.Finite(velocity.sqrMagnitude) || !BallInteractionSystem.Finite(seconds) || seconds <= 0 || seconds > 60 ||
                items.Count >= 256 || nextId == int.MaxValue || (callback != null && callback.Length > 64) ||
                !SimulationContext.IsFinite(position + velocity * seconds) ||
                (c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds)) return 0;
            int id = ++nextId;
            items.Add(id, new Item { Id = id, Source = source, Round = c.Round.RoundId, Origin = position, Velocity = velocity,
                StartedAt = c.Round.Time, Duration = seconds, Callback = callback, NodePath = path });
            return id;
        }
        internal void Tick(SimulationContext c)
        {
            if (items.Count == 0) return;
            ticking.Clear();
            ticking.AddRange(items.Keys);
            ticking.Sort();
            foreach (int id in ticking)
            {
                if (!items.TryGetValue(id, out var item)) continue;
                if (!c.IsRunning || item.Round != c.Round.RoundId || !DeviceDurabilitySystem.CanRun(c, item.Source) ||
                    (c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds))
                { items.Remove(id); continue; }
                if (c.Round.Time - item.StartedAt + 1e-7 < item.Duration) continue;
                items.Remove(id);
                if (!string.IsNullOrEmpty(item.Callback)) DeviceScriptSystem.DeviceEvent(c, item.Source, item.Callback, item);
            }
        }
        internal void Clear() => items.Clear();
        internal void RemoveSource(int source)
        {
            removing.Clear();
            foreach (var item in items.Values)
                if (item.Source == source) removing.Add(item.Id);
            foreach (int id in removing) items.Remove(id);
        }
    }
}
