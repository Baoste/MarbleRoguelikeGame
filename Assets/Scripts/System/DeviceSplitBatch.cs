using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Atomic equal division of one ball. Lua supplies world-space output positions and velocities.</summary>
    [Preserve]
    public sealed class DeviceSplitBatch
    {
        private struct Item { internal Vector3 Position, Velocity; }
        private readonly Func<SimulationContext> context;
        private readonly Entity parent;
        private readonly int sourceId, targetCount;
        private readonly double[] original, values;
        private readonly float radius;
        private readonly List<Item> items = new List<Item>();
        private bool submitted;
        private readonly bool registerRush;
        internal DeviceSplitBatch(Func<SimulationContext> context, Entity parent, int sourceId, int count, bool registerRush = true)
        {
            this.context = context; this.parent = parent; this.sourceId = sourceId;
            this.registerRush = registerRush;
            var c = context();
            if (count < 2 || count > 256) throw new ArgumentOutOfRangeException(nameof(count));
            targetCount = count;
            var attributes = c.Manager.GetBuffer<AttributeValue>(parent);
            original = new double[attributes.Length];
            for (int i = 0; i < original.Length; i++) original[i] = attributes[i].Current;
            values = (double[])original.Clone();
            values[(int)GameAttribute.BALL_BASE_VALUE] /= count;
            values[(int)GameAttribute.BALL_RADIUS] *= Math.Pow(1d / count, 1d / 3d);
            values[(int)GameAttribute.BALL_MASS] /= count;
            radius = (float)values[(int)GameAttribute.BALL_RADIUS] * AttributeRuntime.RadiusToWorld;
        }
        public int Count { get { _ = context(); return items.Count; } }
        public int TargetCount { get { _ = context(); return targetCount; } }
        public float Radius { get { _ = context(); return radius; } }
        public double Value { get { _ = context(); return values[(int)GameAttribute.BALL_BASE_VALUE]; } }
        public float Mass { get { _ = context(); return (float)values[(int)GameAttribute.BALL_MASS]; } }
        public bool CanSplit
        {
            get
            {
                var c = context();
                if (submitted || !c.IsRunning || !c.Manager.Exists(parent) || c.Manager.HasComponent<HeldMotion>(parent) ||
                    c.Manager.GetComponentData<SettlementState>(parent).IsSettled || c.Manager.GetComponentData<DespawnState>(parent).PendingDespawn ||
                    c.Manager.GetComponentData<BallFeatures>(parent).Generation >= c.Content.MaxSplitGeneration ||
                    !BallInteractionSystem.Finite(c.StepSeconds) || c.StepSeconds <= 0 ||
                    c.Round.ActiveMarbleCount - ContentSpawnSystem.PendingCount(c) - 1 + targetCount > c.Tuning.MaxActiveMarbles ||
                    !CampaignStateUtility.FindDevice(c, sourceId, out var source) || !DeviceDurabilitySystem.CanRun(c, source) ||
                    !(c.Physics is IMarblePhysicsCommands commands) || !commands.TryGetDevicePose(sourceId, out _) ||
                    (c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds)) return false;
                var attributes = c.Manager.GetBuffer<AttributeValue>(parent);
                for (int i = 0; i < values.Length; i++)
                {
                    var definition = c.Content.Definition((GameAttribute)i);
                    // Refuse a split that would inflate value/volume/mass through lower-bound clamping.
                    if (attributes[i].Current != original[i] || !MarbleRules.IsFinite(values[i]) ||
                        values[i] < definition.MinValue || values[i] > definition.MaxValue) return false;
                }
                return true;
            }
        }
        public bool TryAdd(Vector3 position, Vector3 velocity)
        {
            _ = context();
            if (submitted || items.Count >= targetCount || !SimulationContext.IsFinite(position) ||
                !SimulationContext.IsFinite(velocity) || !BallInteractionSystem.Finite(velocity.sqrMagnitude)) return false;
            foreach (var item in items)
                if (Vector3.Distance(position, item.Position) < radius * 2 + .001f) return false;
            items.Add(new Item { Position = position, Velocity = velocity }); return true;
        }
        public bool TryCommit()
        {
            var c = context();
            bool valid = CanSplit && items.Count == targetCount;
            submitted = true;
            if (!valid) return false;
            var features = c.Manager.GetComponentData<BallFeatures>(parent);
            features.Generation++; features.SplitConsumed = true; features.MovedTick = c.Round.Tick;
            features.SpawnSourceId = sourceId;
            var rotation = c.Manager.GetComponentData<Transform3D>(parent).Rotation;
            var score = c.Manager.GetComponentData<MarbleScore>(parent);
            score.BaseScore = Value; score.ScoreBonus /= targetCount;
            var rush = c.Manager.GetComponentData<MarbleRush>(parent);
            var quote = new ShotQuote(0, 0, radius, Mass, Value, score.BaseMultiplier);
            var spawn = new AttributeRuntime.Spawn { Values = values, Features = features, Quote = quote, Count = 1 };
            using var visits = c.Manager.GetBuffer<DeviceVisit>(parent).ToNativeArray(Allocator.Temp);
            using var sources = c.Manager.GetBuffer<DeviceSplitSource>(parent).ToNativeArray(Allocator.Temp);
            using var copies = c.Manager.GetBuffer<DeviceCopySource>(parent).ToNativeArray(Allocator.Temp);
            using var legacyVisits = c.Manager.GetBuffer<VisitedDeviceData>(parent).ToNativeArray(Allocator.Temp);
            using var modifiers = c.Manager.GetBuffer<MarbleModifier>(parent).ToNativeArray(Allocator.Temp);
            var created = new List<Entity>(); bool committed = false;
            try
            {
                foreach (var item in items)
                {
                    if (!ContentSpawnSystem.Create(c, spawn, item.Position, rotation, item.Velocity, out var child, parent)) return false;
                    created.Add(child);
                    c.Manager.SetComponentData(child, score); c.Manager.SetComponentData(child, rush);
                    MarbleAppearanceSystem.Copy(c, parent, child);
                    c.Manager.GetBuffer<DeviceVisit>(child).CopyFrom(visits);
                    c.Manager.GetBuffer<DeviceCopySource>(child).CopyFrom(copies);
                    c.Manager.GetBuffer<VisitedDeviceData>(child).CopyFrom(legacyVisits);
                    var history = c.Manager.GetBuffer<DeviceSplitSource>(child); history.CopyFrom(sources);
                    bool known = false; foreach (var entry in history) if (entry.InstanceId == sourceId) known = true;
                    if (!known) history.Add(new DeviceSplitSource { InstanceId = sourceId });
                    var target = c.Manager.GetBuffer<MarbleModifier>(child);
                    foreach (var entry in modifiers)
                    {
                        var divided = entry;
                        if (divided.Attribute == MarbleModifierAttribute.ScoreAdd) divided.Value /= targetCount;
                        target.Add(divided);
                    }
                    c.Manager.SetComponentData(child, c.Manager.GetComponentData<Lifetime>(parent));
                    if (c.Manager.HasComponent<TimedGravity>(parent))
                    {
                        c.Manager.AddComponentData(child, c.Manager.GetComponentData<TimedGravity>(parent));
                        var shot = c.Manager.GetComponentData<ShotOrigin>(child);
                        ((IMarblePhysicsCommands)c.Physics).SetProperties(new MarbleKey(shot.RoundId, shot.SpawnSequence), AttributeRuntime.Properties(c, child));
                    }
                    BallInteractionSystem.RecordDeviceEffect(c, child, sourceId);
                }
                // Keep the parent until every child exists. Normal end-of-step cleanup removes it without a refund.
                RushSystem.RecordSpawn(c, parent, created, sourceId, registerRush);
                BallInteractionSystem.Consume(c, parent, false);
                committed = true; return true;
            }
            finally { if (!committed) foreach (var child in created) ContentSpawnSystem.Rollback(c, child); }
        }
    }
}
