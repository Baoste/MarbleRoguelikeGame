using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Callback-scoped, atomic creation of independent marbles. Lua supplies the layout and velocities.</summary>
    [Preserve]
    public sealed class DeviceSpawnBatch
    {
        private struct Item { internal Vector3 Position, Velocity; internal double Value; }
        private readonly Func<SimulationContext> context;
        private readonly int sourceId;
        private readonly double[] defaults;
        private readonly List<Item> items = new List<Item>();
        private readonly float radius, mass;
        private bool submitted;
        internal DeviceSpawnBatch(Func<SimulationContext> context, int sourceId)
        {
            this.context = context; this.sourceId = sourceId;
            var c = context();
            defaults = new double[(int)GameAttribute.Count];
            for (int i = 0; i < defaults.Length; i++) defaults[i] = c.Content.Definition((GameAttribute)i).DefaultValue;
            radius = (float)defaults[(int)GameAttribute.BALL_RADIUS] * AttributeRuntime.RadiusToWorld;
            mass = (float)defaults[(int)GameAttribute.BALL_MASS];
        }
        public float Radius { get { _ = context(); return radius; } }
        public int Count { get { _ = context(); return items.Count; } }
        /// <summary>Queues a world position, nonzero velocity and base value. No entity is created yet.</summary>
        public bool TryAdd(Vector3 position, Vector3 velocity, double value)
        {
            _ = context();
            if (submitted || items.Count >= 256 || !SimulationContext.IsFinite(position) ||
                !SimulationContext.IsFinite(velocity) || !BallInteractionSystem.Finite(velocity.sqrMagnitude) ||
                velocity.sqrMagnitude < 1e-12f || !MarbleRules.IsFinite(value) || value < 0) return false;
            foreach (var item in items)
                if (Vector3.Distance(position, item.Position) < radius * 2 + .001f) return false;
            items.Add(new Item { Position = position, Velocity = velocity, Value = value });
            return true;
        }
        /// <summary>One submission per batch; any failed spawn rolls the entire batch back.</summary>
        public bool TryCommit()
        {
            var c = context();
            if (submitted) return false;
            submitted = true;
            if (items.Count == 0 || !c.IsRunning || !BallInteractionSystem.Finite(c.StepSeconds) || c.StepSeconds <= 0 ||
                c.Round.ActiveMarbleCount + items.Count > c.Tuning.MaxActiveMarbles ||
                !CampaignStateUtility.FindDevice(c, sourceId, out var source) ||
                !c.Manager.GetComponentData<OwnedDeviceData>(source).Placed ||
                !(c.Physics is IMarblePhysicsCommands commands) || !commands.TryGetDevicePose(sourceId, out _) ||
                (c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds)) return false;
            var created = new List<Entity>();
            bool committed = false;
            try
            {
                foreach (var item in items)
                {
                    var values = (double[])defaults.Clone();
                    values[(int)GameAttribute.BALL_BASE_VALUE] = c.Content.Definition(GameAttribute.BALL_BASE_VALUE).Clamp(item.Value);
                    var spawn = new AttributeRuntime.Spawn
                    {
                        Values = values, Count = 1,
                        Features = new BallFeatures { SpawnSourceId = sourceId },
                        Quote = new ShotQuote(0, 0, radius, mass, values[(int)GameAttribute.BALL_BASE_VALUE], 1)
                    };
                    if (!ContentSpawnSystem.Create(c, spawn, item.Position, Quaternion.identity, item.Velocity, out var ball)) return false;
                    created.Add(ball);
                }
                committed = true;
                return true;
            }
            finally { if (!committed) foreach (var ball in created) ContentSpawnSystem.Rollback(c, ball); }
        }
    }
}
