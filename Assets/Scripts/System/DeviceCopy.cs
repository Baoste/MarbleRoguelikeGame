using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Callback-scoped, single-use copy request. Output geometry and value policy belong to Lua.</summary>
    [Preserve]
    public sealed class DeviceCopy
    {
        private readonly Func<SimulationContext> context;
        private readonly Entity parent;
        private readonly int sourceId;
        private readonly int count;
        private readonly List<(Vector3 position, Vector3 velocity)> outputs;
        private double? valueOverride;
        private double? deviceMultiplierOverride;
        private bool submitted;
        private readonly bool registerRush;
        internal DeviceCopy(Func<SimulationContext> context, Entity parent, int sourceId, int count, bool registerRush)
        {
            if (count < 1 || count > 20) throw new ArgumentOutOfRangeException(nameof(count));
            this.context = context; this.parent = parent; this.sourceId = sourceId; this.count = count;
            this.registerRush = registerRush;
            outputs = new List<(Vector3, Vector3)>(count);
        }
        public double Value { get { var c = context(); return valueOverride ?? AttributeRuntime.Get(c, parent, GameAttribute.BALL_BASE_VALUE); } }
        public float Radius => (float)AttributeRuntime.Get(context(), parent, GameAttribute.BALL_RADIUS);
        public bool CanCopy
        {
            get
            {
                var c = context();
                return !submitted && c.IsRunning && c.Manager.Exists(parent) && !c.Manager.HasComponent<HeldMotion>(parent) &&
                    !c.Manager.GetComponentData<SettlementState>(parent).IsSettled && !c.Manager.GetComponentData<DespawnState>(parent).PendingDespawn &&
                    c.Manager.GetComponentData<BallFeatures>(parent).ReplicationGeneration < c.Content.MaxSplitGeneration &&
                    BallInteractionSystem.Finite(c.StepSeconds) && c.StepSeconds > 0 &&
                    c.Round.ActiveMarbleCount <= c.Tuning.MaxActiveMarbles - count &&
                    CampaignStateUtility.FindDevice(c, sourceId, out var source) && DeviceDurabilitySystem.CanRun(c, source) &&
                    c.Physics is IMarblePhysicsCommands commands && commands.TryGetDevicePose(sourceId, out _) &&
                    !(c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds);
            }
        }
        /// <summary>Replaces copied base value and removes inherited flat score/value adjustments, keeping multipliers.</summary>
        public bool TrySetValue(double value)
        {
            var c = context(); var definition = c.Content.Definition(GameAttribute.BALL_BASE_VALUE);
            if (submitted || !MarbleRules.IsFinite(value) || value < definition.MinValue || value > definition.MaxValue) return false;
            valueOverride = value; return true;
        }
        /// <summary>Replaces only the copied ball's accumulated device multiplier; Lua chooses the formula.</summary>
        public bool TrySetDeviceScoreMultiplier(double amount)
        {
            _ = context();
            if (submitted || !MarbleRules.IsFinite(amount) || amount < 0 || amount > 1000) return false;
            deviceMultiplierOverride = amount; return true;
        }
        public bool TrySpawn(Vector3 position, Vector3 velocity)
        {
            if (TryAddSpawn(position, velocity)) return TryCommit();
            submitted = true;
            return false;
        }

        public bool TryAddSpawn(Vector3 position, Vector3 velocity)
        {
            _ = context();
            if (submitted || outputs.Count == count || !SimulationContext.IsFinite(position) ||
                !SimulationContext.IsFinite(velocity) || !BallInteractionSystem.Finite(velocity.sqrMagnitude)) return false;
            outputs.Add((position, velocity));
            return true;
        }

        public bool TryCommit()
        {
            var c = context();
            bool valid = CanCopy && outputs.Count == count;
            submitted = true;
            if (!valid) return false;
            using var attributes = c.Manager.GetBuffer<AttributeValue>(parent).ToNativeArray(Allocator.Temp);
            using var effects = c.Manager.GetBuffer<AttributeModifierData>(parent).ToNativeArray(Allocator.Temp);
            using var modifiers = c.Manager.GetBuffer<MarbleModifier>(parent).ToNativeArray(Allocator.Temp);
            using var copies = c.Manager.GetBuffer<DeviceCopySource>(parent).ToNativeArray(Allocator.Temp);
            using var splits = c.Manager.GetBuffer<DeviceSplitSource>(parent).ToNativeArray(Allocator.Temp);
            using var visits = c.Manager.GetBuffer<DeviceVisit>(parent).ToNativeArray(Allocator.Temp);
            var values = new double[attributes.Length];
            for (int i = 0; i < values.Length; i++) values[i] = attributes[i].Current;
            if (valueOverride.HasValue) values[(int)GameAttribute.BALL_BASE_VALUE] = valueOverride.Value;
            var score = c.Manager.GetComponentData<MarbleScore>(parent);
            score.BaseScore = values[(int)GameAttribute.BALL_BASE_VALUE];
            if (valueOverride.HasValue) score.ScoreBonus = 0;
            if (deviceMultiplierOverride.HasValue)
                score.SetDeviceMultiplier(deviceMultiplierOverride.Value);
            var features = c.Manager.GetComponentData<BallFeatures>(parent);
            features.ReplicationGeneration++; features.SpawnSourceId = sourceId; features.MovedTick = c.Round.Tick;
            var quote = new ShotQuote(0, 0, Radius, (float)values[(int)GameAttribute.BALL_MASS], score.BaseScore, score.BaseMultiplier);
            var spawn = new AttributeRuntime.Spawn { Values = values, Features = features, Quote = quote, Count = 1 };
            var rotation = c.Manager.GetComponentData<Transform3D>(parent).Rotation;
            var created = new List<Entity>(count);
            bool committed = false;
            try
            {
                foreach (var output in outputs)
                {
                    if (!ContentSpawnSystem.Create(c, spawn, output.position, rotation, output.velocity, out var child)) return false;
                    created.Add(child);
                    c.Manager.SetComponentData(child, score);
                    c.Manager.SetComponentData(child, c.Manager.GetComponentData<MarbleRush>(parent));
                    MarbleAppearanceSystem.Copy(c, parent, child);
                    c.Manager.SetComponentData(child, c.Manager.GetComponentData<Lifetime>(parent));
                    var targetAttributes = c.Manager.GetBuffer<AttributeValue>(child);
                    targetAttributes.CopyFrom(attributes);
                    if (valueOverride.HasValue)
                        targetAttributes[(int)GameAttribute.BALL_BASE_VALUE] = new AttributeValue { Base = valueOverride.Value, Current = valueOverride.Value };
                    var targetEffects = c.Manager.GetBuffer<AttributeModifierData>(child);
                    foreach (var entry in effects)
                        if (!valueOverride.HasValue || entry.Effect.Attribute != GameAttribute.BALL_BASE_VALUE) targetEffects.Add(entry);
                    var targetModifiers = c.Manager.GetBuffer<MarbleModifier>(child);
                    foreach (var entry in modifiers)
                        if (!valueOverride.HasValue || entry.Attribute != MarbleModifierAttribute.ScoreAdd) targetModifiers.Add(entry);
                    c.Manager.GetBuffer<DeviceSplitSource>(child).CopyFrom(splits);
                    var history = c.Manager.GetBuffer<DeviceCopySource>(child);
                    history.CopyFrom(copies);
                    bool known = false;
                    foreach (var entry in history) if (entry.InstanceId == sourceId) known = true;
                    if (!known) history.Add(new DeviceCopySource { InstanceId = sourceId });
                    if (c.Manager.HasComponent<TimedGravity>(parent))
                    {
                        c.Manager.AddComponentData(child, c.Manager.GetComponentData<TimedGravity>(parent));
                        var shot = c.Manager.GetComponentData<ShotOrigin>(child);
                        ((IMarblePhysicsCommands)c.Physics).SetProperties(new MarbleKey(shot.RoundId, shot.SpawnSequence), AttributeRuntime.Properties(c, child));
                    }
                    var childVisits = c.Manager.GetBuffer<DeviceVisit>(child);
                    foreach (var entry in visits) if (entry.InheritOnCopy) childVisits.Add(entry);
                    BallInteractionSystem.RecordDeviceEffect(c, child, sourceId);
                }
                BallInteractionSystem.RecordDeviceEffect(c, parent, sourceId);
                RushSystem.RecordSpawn(c, parent, created, sourceId, registerRush);
                committed = true; return true;
            }
            finally { if (!committed) foreach (var child in created) ContentSpawnSystem.Rollback(c, child); }
        }
    }
}
