using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal static class ContentSpawnSystem
    {
        internal static void Launch(SimulationContext c, Vector3 position, Quaternion rotation)
        {
            var round = c.Round;
            var launcher = c.Launcher;
            if (round.Phase != (byte)RoundPhase.Playing || (launcher.FireHeld == 0 && launcher.PendingSingleShots == 0) || round.Time < launcher.NextFireAt) return;
            var spawn = AttributeRuntime.Prepare(c, launcher.Flow);
            double cost = spawn.Count * spawn.Quote.Cost;
            if (round.ActiveMarbleCount + spawn.Count > c.Tuning.MaxActiveMarbles || c.Player.Blood < cost) return;
            var created = new List<Entity>();
            bool committed = false;
            try
            {
                for (int i = 0; i < spawn.Count; i++)
                {
                    Vector3 offset = rotation * Vector3.right * ((i - (spawn.Count - 1) * .5f) * (spawn.Quote.Radius * 2 + .01f));
                    Vector3 velocity = rotation * Vector3.down * (float)spawn.Values[(int)GameAttribute.BALL_LAUNCH_SPEED];
                    if (!Create(c, spawn, position + offset, rotation, velocity, out var entity)) return;
                    created.Add(entity);
                }
                var player = c.Player;
                player.Blood -= (float)cost;
                c.Player = player;
                launcher.NextFireAt = round.Time + c.Tuning.FireIntervalSeconds;
                if (launcher.PendingSingleShots > 0) launcher.PendingSingleShots--;
                c.Launcher = launcher;
                committed = true;
            }
            finally { if (!committed) foreach (var entity in created) Rollback(c, entity); }
        }

        internal static bool Create(SimulationContext c, AttributeRuntime.Spawn spawn, Vector3 position,
            Quaternion rotation, Vector3 velocity, out Entity entity)
        {
            entity = Entity.Null;
            if (c.Round.ActiveMarbleCount >= c.Tuning.MaxActiveMarbles) return false;
            var key = new MarbleKey(c.Round.RoundId, checked(++c.NextSpawnSequence));
            Entity candidate = MarbleFactory.Create(c, key, spawn.Quote);
            bool committed = false;
            try
            {
                AttributeRuntime.Initialize(c, candidate, spawn.Values);
                var features = spawn.Features;
                if (features.Family == 0) { features.Family = key.Sequence; features.RefundBlood = spawn.Quote.Cost; features.BirthTick = c.Round.Tick; }
                c.Manager.AddComponentData(candidate, features);
                c.Manager.AddBuffer<DeviceVisit>(candidate);
                c.Manager.SetComponentData(candidate, new Lifetime { ExpireTick = c.Round.Tick + (long)Math.Ceiling(
                    spawn.Values[(int)GameAttribute.BALL_LIFETIME] / c.StepSeconds) });
                var settings = AttributeRuntime.Properties(c, candidate);
                velocity = Vector3.ClampMagnitude(velocity, settings.MaxSpeed);
                if (!c.Physics.TrySpawn(new MarbleSpawnData { Key = key, Position = position, Rotation = rotation,
                    Quote = spawn.Quote, LinearVelocity = velocity, HasRestitution = true, Restitution = Mathf.Min(1, settings.Bounce),
                    HasSettings = true, Settings = settings })) return false;
                c.Manager.SetComponentData(candidate, new Transform3D { Position = position, Rotation = rotation });
                c.Manager.SetComponentData(candidate, new Motion3D { LinearVelocity = velocity });
                c.Marbles.Add(key, candidate);
                var round = c.Round; round.ActiveMarbleCount++; c.Round = round;
                entity = candidate;
                committed = true;
                return true;
            }
            finally
            {
                if (!committed) { c.Physics.Remove(key); c.Manager.DestroyEntity(candidate); }
            }
        }

        internal static void Rollback(SimulationContext c, Entity entity)
        {
            var shot = c.Manager.GetComponentData<ShotOrigin>(entity);
            var key = new MarbleKey(shot.RoundId, shot.SpawnSequence);
            c.Physics.Remove(key); c.Marbles.Remove(key); c.Manager.DestroyEntity(entity);
            var round = c.Round; round.ActiveMarbleCount--; c.Round = round;
        }

        internal static bool Children(SimulationContext c, Entity parent, int count, bool replace, Vector3 direction, Vector3 normal)
        {
            var features = c.Manager.GetComponentData<BallFeatures>(parent);
            if (count < 1 || c.Round.ActiveMarbleCount + count > c.Tuning.MaxActiveMarbles ||
                (replace && features.Generation >= c.Content.MaxSplitGeneration)) return false;
            var attributes = c.Manager.GetBuffer<AttributeValue>(parent);
            var values = new double[attributes.Length];
            for (int i = 0; i < values.Length; i++) values[i] = attributes[i].Current;
            var state = c.Manager.GetComponentData<Transform3D>(parent);
            var motion = c.Manager.GetComponentData<Motion3D>(parent);
            double retained = replace ? values[(int)GameAttribute.BALL_CLONE_VALUE_RETENTION] : 1;
            values[(int)GameAttribute.BALL_BASE_VALUE] *= retained;
            if (replace)
            {
                values[(int)GameAttribute.BALL_RADIUS] *= Math.Pow(1d / count, 1d / 3d);
                values[(int)GameAttribute.BALL_MASS] /= count;
                features.Generation++;
                features.SplitConsumed = true;
            }
            float radius = (float)values[(int)GameAttribute.BALL_RADIUS] * AttributeRuntime.RadiusToWorld;
            var origin = c.Manager.GetComponentData<ShotOrigin>(parent);
            var quote = new ShotQuote(origin.BloodInvestment, 0, radius, (float)values[(int)GameAttribute.BALL_MASS],
                values[(int)GameAttribute.BALL_BASE_VALUE], c.Manager.GetComponentData<MarbleScore>(parent).ScoreMultiplier);
            var spawn = new AttributeRuntime.Spawn { Values = values, Features = features, Quote = quote, Count = 1 };
            var children = new List<Entity>();
            var visits = c.Manager.GetBuffer<DeviceVisit>(parent).ToNativeArray(Unity.Collections.Allocator.Temp);
            var legacyVisits = c.Manager.GetBuffer<VisitedDeviceData>(parent).ToNativeArray(Unity.Collections.Allocator.Temp);
            var attributeCopy = c.Manager.GetBuffer<AttributeValue>(parent).ToNativeArray(Unity.Collections.Allocator.Temp);
            var effectCopy = c.Manager.GetBuffer<AttributeModifierData>(parent).ToNativeArray(Unity.Collections.Allocator.Temp);
            var modifierCopy = c.Manager.GetBuffer<MarbleModifier>(parent).ToNativeArray(Unity.Collections.Allocator.Temp);
            var parentScore = c.Manager.GetComponentData<MarbleScore>(parent);
            var parentRush = c.Manager.GetComponentData<MarbleRush>(parent);
            long expiry = c.Manager.GetComponentData<Lifetime>(parent).ExpireTick;
            bool success = false;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    float angle = count == 1 ? 55 : Mathf.Lerp(-45, 45, (float)i / Math.Max(1, count - 1));
                    Vector3 output = Quaternion.AngleAxis(angle, normal) * direction.normalized;
                    Vector3 position = state.Position + output * (radius + (float)AttributeRuntime.Get(c, parent, GameAttribute.BALL_RADIUS) * AttributeRuntime.RadiusToWorld + .02f);
                    if (!Create(c, spawn, position, state.Rotation, output * Mathf.Max(1, motion.LinearVelocity.magnitude), out var child)) return false;
                    children.Add(child);
                    c.Manager.GetBuffer<DeviceVisit>(child).CopyFrom(visits);
                    c.Manager.GetBuffer<VisitedDeviceData>(child).CopyFrom(legacyVisits);
                    c.Manager.GetBuffer<MarbleModifier>(child).CopyFrom(modifierCopy);
                    c.Manager.SetComponentData(child, parentRush);
                    var childScore = parentScore;
                    childScore.BaseScore = values[(int)GameAttribute.BALL_BASE_VALUE];
                    childScore.ScoreBonus *= retained;
                    c.Manager.SetComponentData(child, childScore);
                    if (!replace)
                    {
                        c.Manager.GetBuffer<AttributeValue>(child).CopyFrom(attributeCopy);
                        c.Manager.GetBuffer<AttributeModifierData>(child).CopyFrom(effectCopy);
                    }
                    c.Manager.SetComponentData(child, new Lifetime { ExpireTick = expiry });
                }
                success = true;
                if (replace) DeviceEffectSystem.Consume(c, parent, false);
                return true;
            }
            finally
            {
                visits.Dispose(); legacyVisits.Dispose(); attributeCopy.Dispose(); effectCopy.Dispose(); modifierCopy.Dispose();
                if (!success) foreach (var child in children) Rollback(c, child);
            }
        }
    }
}
