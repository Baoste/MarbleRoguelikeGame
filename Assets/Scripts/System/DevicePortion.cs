using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Atomically transfers part of a ball's value into one new ball; Lua selects size and output motion.</summary>
    [Preserve]
    public sealed class DevicePortion
    {
        private readonly Func<SimulationContext> context;
        private readonly Entity parent;
        private readonly int sourceId;
        private readonly double value;
        private float radius, mass;
        private bool submitted;
        private readonly bool registerRush;
        internal DevicePortion(Func<SimulationContext> context, Entity parent, int sourceId, double value, bool registerRush = true)
        {
            this.context = context; this.parent = parent; this.sourceId = sourceId;
            this.registerRush = registerRush;
            var c = context();
            if (!MarbleRules.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            this.value = value;
            radius = (float)AttributeRuntime.Get(c, parent, GameAttribute.BALL_RADIUS) * AttributeRuntime.RadiusToWorld;
            mass = (float)AttributeRuntime.Get(c, parent, GameAttribute.BALL_MASS);
        }
        public double Value { get { _ = context(); return value; } }
        public float Radius { get { _ = context(); return radius; } }
        public float Mass { get { _ = context(); return mass; } }
        public bool TrySetSize(float worldRadius, float mass)
        {
            var c = context();
            if (submitted || !BallInteractionSystem.Finite(worldRadius) || !BallInteractionSystem.Finite(mass) ||
                !InRange(c, GameAttribute.BALL_RADIUS, worldRadius / AttributeRuntime.RadiusToWorld) ||
                !InRange(c, GameAttribute.BALL_MASS, mass)) return false;
            radius = worldRadius; this.mass = mass; return true;
        }
        private static bool InRange(SimulationContext c, GameAttribute id, double candidate)
        { var d = c.Content.Definition(id); return candidate >= d.MinValue && candidate <= d.MaxValue; }
        public bool CanSplit
        {
            get
            {
                var c = context();
                if (submitted || !c.IsRunning || !c.Manager.Exists(parent) || c.Manager.HasComponent<HeldMotion>(parent) ||
                    c.Manager.GetComponentData<SettlementState>(parent).IsSettled || c.Manager.GetComponentData<DespawnState>(parent).PendingDespawn ||
                    c.Manager.GetComponentData<BallFeatures>(parent).Generation >= c.Content.MaxSplitGeneration ||
                    !BallInteractionSystem.Finite(c.StepSeconds) || c.StepSeconds <= 0 ||
                    !CampaignStateUtility.FindDevice(c, sourceId, out var source) || !DeviceDurabilitySystem.CanRun(c, source) ||
                    !(c.Physics is IMarblePhysicsCommands commands) || !commands.TryGetDevicePose(sourceId, out _) ||
                    (c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds)) return false;
                double total = AttributeRuntime.Get(c, parent, GameAttribute.BALL_BASE_VALUE);
                if (value > total || !InRange(c, GameAttribute.BALL_BASE_VALUE, value) ||
                    !InRange(c, GameAttribute.BALL_BASE_VALUE, total - value)) return false;
                return value == total
                    ? c.Round.ActiveMarbleCount - ContentSpawnSystem.PendingCount(c) <= c.Tuning.MaxActiveMarbles
                    : c.Round.ActiveMarbleCount < c.Tuning.MaxActiveMarbles;
            }
        }
        public bool TrySpawn(Vector3 position, Vector3 velocity)
        {
            var c = context(); bool valid = CanSplit; submitted = true;
            if (!valid || !SimulationContext.IsFinite(position) || !SimulationContext.IsFinite(velocity) ||
                !BallInteractionSystem.Finite(velocity.sqrMagnitude)) return false;
            using var attributes = c.Manager.GetBuffer<AttributeValue>(parent).ToNativeArray(Allocator.Temp);
            using var effects = c.Manager.GetBuffer<AttributeModifierData>(parent).ToNativeArray(Allocator.Temp);
            using var modifiers = c.Manager.GetBuffer<MarbleModifier>(parent).ToNativeArray(Allocator.Temp);
            using var visits = c.Manager.GetBuffer<DeviceVisit>(parent).ToNativeArray(Allocator.Temp);
            using var legacyVisits = c.Manager.GetBuffer<VisitedDeviceData>(parent).ToNativeArray(Allocator.Temp);
            using var splits = c.Manager.GetBuffer<DeviceSplitSource>(parent).ToNativeArray(Allocator.Temp);
            using var copies = c.Manager.GetBuffer<DeviceCopySource>(parent).ToNativeArray(Allocator.Temp);
            double total = attributes[(int)GameAttribute.BALL_BASE_VALUE].Current, fraction = value / total;
            var values = new double[attributes.Length];
            for (int i = 0; i < values.Length; i++) values[i] = attributes[i].Current;
            values[(int)GameAttribute.BALL_BASE_VALUE] = value;
            values[(int)GameAttribute.BALL_RADIUS] = radius / AttributeRuntime.RadiusToWorld;
            values[(int)GameAttribute.BALL_MASS] = mass;
            var parentScore = c.Manager.GetComponentData<MarbleScore>(parent); var childScore = parentScore;
            childScore.BaseScore = value; childScore.ScoreBonus *= fraction;
            var features = c.Manager.GetComponentData<BallFeatures>(parent);
            features.Generation++; features.SplitConsumed = true; features.SpawnSourceId = sourceId; features.MovedTick = c.Round.Tick;
            var spawn = new AttributeRuntime.Spawn { Values = values, Features = features, Count = 1,
                Quote = new ShotQuote(0, 0, radius, mass, value, childScore.BaseMultiplier) };
            bool last = total == value;
            if (!ContentSpawnSystem.Create(c, spawn, position, c.Manager.GetComponentData<Transform3D>(parent).Rotation,
                velocity, out var child, last ? parent : Entity.Null)) return false;
            bool committed = false;
            try
            {
                c.Manager.SetComponentData(child, childScore);
                c.Manager.SetComponentData(child, c.Manager.GetComponentData<MarbleRush>(parent));
                MarbleAppearanceSystem.Copy(c, parent, child);
                c.Manager.SetComponentData(child, c.Manager.GetComponentData<Lifetime>(parent));
                var childAttributes = c.Manager.GetBuffer<AttributeValue>(child); childAttributes.CopyFrom(attributes);
                foreach (var id in new[] { GameAttribute.BALL_BASE_VALUE, GameAttribute.BALL_RADIUS, GameAttribute.BALL_MASS })
                    childAttributes[(int)id] = new AttributeValue { Base = values[(int)id], Current = values[(int)id] };
                var childEffects = c.Manager.GetBuffer<AttributeModifierData>(child);
                foreach (var effect in effects)
                    if (effect.Effect.Attribute != GameAttribute.BALL_BASE_VALUE && effect.Effect.Attribute != GameAttribute.BALL_RADIUS &&
                        effect.Effect.Attribute != GameAttribute.BALL_MASS) childEffects.Add(effect);
                var childModifiers = c.Manager.GetBuffer<MarbleModifier>(child);
                foreach (var entry in modifiers)
                { var part = entry; if (part.Attribute == MarbleModifierAttribute.ScoreAdd) part.Value *= fraction; childModifiers.Add(part); }
                c.Manager.GetBuffer<DeviceVisit>(child).CopyFrom(visits);
                c.Manager.GetBuffer<VisitedDeviceData>(child).CopyFrom(legacyVisits);
                c.Manager.GetBuffer<DeviceCopySource>(child).CopyFrom(copies);
                var history = c.Manager.GetBuffer<DeviceSplitSource>(child); history.CopyFrom(splits);
                bool known = false; foreach (var entry in history) if (entry.InstanceId == sourceId) known = true;
                if (!known) history.Add(new DeviceSplitSource { InstanceId = sourceId });
                if (c.Manager.HasComponent<TimedGravity>(parent))
                {
                    c.Manager.AddComponentData(child, c.Manager.GetComponentData<TimedGravity>(parent));
                    var shot = c.Manager.GetComponentData<ShotOrigin>(child);
                    ((IMarblePhysicsCommands)c.Physics).SetProperties(new MarbleKey(shot.RoundId, shot.SpawnSequence), AttributeRuntime.Properties(c, child));
                }
                // Debit only after all child setup succeeds. Failed spawns never consume value or flat bonuses.
                var parentEffects = c.Manager.GetBuffer<AttributeModifierData>(parent);
                for (int i = parentEffects.Length - 1; i >= 0; i--)
                    if (parentEffects[i].Effect.Attribute == GameAttribute.BALL_BASE_VALUE) parentEffects.RemoveAt(i);
                var parentAttributes = c.Manager.GetBuffer<AttributeValue>(parent);
                parentAttributes[(int)GameAttribute.BALL_BASE_VALUE] =
                    new AttributeValue { Base = total - value, Current = total - value };
                parentScore.BaseScore = total - value; parentScore.ScoreBonus -= childScore.ScoreBonus;
                c.Manager.SetComponentData(parent, parentScore);
                var parentModifiers = c.Manager.GetBuffer<MarbleModifier>(parent);
                for (int i = 0; i < parentModifiers.Length; i++)
                {
                    var entry = parentModifiers[i];
                    if (entry.Attribute == MarbleModifierAttribute.ScoreAdd) { entry.Value -= entry.Value * fraction; parentModifiers[i] = entry; }
                }
                BallInteractionSystem.RecordDeviceEffect(c, parent, sourceId); BallInteractionSystem.RecordDeviceEffect(c, child, sourceId);
                RushSystem.RecordSpawn(c, parent, new[] { child }, sourceId, registerRush);
                if (last) BallInteractionSystem.Consume(c, parent, false);
                committed = true; return true;
            }
            finally { if (!committed) ContentSpawnSystem.Rollback(c, child); }
        }
    }
}
