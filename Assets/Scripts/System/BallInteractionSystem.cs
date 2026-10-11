using System;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal static class BallInteractionSystem
    {
        internal static void Execute(SimulationContext c, Entity ball, MarbleContact contact)
        {
            var features = c.Manager.GetComponentData<BallFeatures>(ball);
            if (features.MovedTick == c.Round.Tick || c.Manager.HasComponent<HeldMotion>(ball)) return;
            if (contact.Kind == MarbleContactKind.Pierced)
            {
                features.PiercesUsed++;
                c.Manager.SetComponentData(ball, features);
                return;
            }
            if (contact.Kind == MarbleContactKind.Pin)
            {
                if (!MarkVisit(c, ball, contact.TargetId, 0)) return;
                if (features.Replicates) Replicate(c, ball, contact);
                int splits = (int)AttributeRuntime.Get(c, ball, GameAttribute.BALL_SPLIT_COUNT);
                if (splits > 0 && !features.SplitConsumed)
                    ContentSpawnSystem.Children(c, ball, splits, true, Direction(c, ball), Vector3.up);
                return;
            }
            DeviceScriptSystem.Contact(c, ball, contact);
        }

        internal static bool MarkVisit(SimulationContext c, Entity ball, long key, int maximum)
        {
            var visits = c.Manager.GetBuffer<DeviceVisit>(ball);
            for (int i = 0; i < visits.Length; i++)
            {
                var visit = visits[i];
                if (visit.Key != key) continue;
                if (visit.LastTick == c.Round.Tick || (maximum > 0 && visit.Count >= maximum)) return false;
                visit.Count++; visit.LastTick = c.Round.Tick; visits[i] = visit; return true;
            }
            visits.Add(new DeviceVisit { Key = key, Count = 1, LastTick = c.Round.Tick }); return true;
        }
        internal static Vector3 Down(DevicePose pose)
        {
            var down = Vector3.ProjectOnPlane(Vector3.down, pose.Up);
            return down.sqrMagnitude > .0001f ? down.normalized : pose.Forward;
        }
        internal static Vector3 Direction(SimulationContext c, Entity ball)
        {
            var velocity = c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity;
            return velocity.sqrMagnitude > .0001f ? velocity.normalized : Vector3.down;
        }
        internal static void Replicate(SimulationContext c, Entity ball, MarbleContact contact)
        {
            var features = c.Manager.GetComponentData<BallFeatures>(ball);
            if (features.ReplicationGeneration >= c.Content.MaxSplitGeneration) return;
            features.ReplicationGeneration++;
            c.Manager.SetComponentData(ball, features);
            Vector3 normal = Vector3.up;
            if (c.Physics is IMarblePhysicsCommands commands && commands.TryGetDevicePose(contact.TargetId - 100000, out var pose)) normal = pose.Up;
            ContentSpawnSystem.Children(c, ball, 1, false, Direction(c, ball), normal);
        }
        internal static bool Exit(SimulationContext c, Entity ball, DevicePose pose, Vector3 direction, float speed)
        {
            if (!ValidExit(pose, direction, speed)) return false;
            float radius = (float)AttributeRuntime.Get(c, ball, GameAttribute.BALL_RADIUS) * AttributeRuntime.RadiusToWorld;
            return Move(c, ball, pose.Position + pose.Up.normalized * (radius + .005f) + direction.normalized * (pose.Radius + radius + .02f), direction.normalized * speed);
        }
        internal static bool ValidExit(DevicePose pose, Vector3 direction, float speed) =>
            Finite(speed) && speed >= 0 && Finite(direction) && Finite(direction.sqrMagnitude) && direction.sqrMagnitude >= 1e-12f &&
            Finite(pose.Position) && Finite(pose.Up) && Finite(pose.Up.sqrMagnitude) && pose.Up.sqrMagnitude >= 1e-12f && Finite(pose.Radius) && pose.Radius >= 0;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        internal static bool Move(SimulationContext c, Entity ball, Vector3 position, Vector3 velocity)
        {
            if (c.Manager.HasComponent<HeldMotion>(ball) || !(c.Physics is IMarblePhysicsCommands commands) || !Finite(position) || !Finite(velocity) || !Finite(velocity.sqrMagnitude)) return false;
            var shot = c.Manager.GetComponentData<ShotOrigin>(ball);
            velocity = Vector3.ClampMagnitude(velocity, (float)AttributeRuntime.Get(c, ball, GameAttribute.BALL_MAX_SPEED));
            if (!commands.TryMove(new MarbleKey(shot.RoundId, shot.SpawnSequence), position, velocity)) return false;
            var pose = c.Manager.GetComponentData<Transform3D>(ball); pose.Position = position;
            c.Manager.SetComponentData(ball, pose);
            SetMotion(c, ball, velocity);
            return true;
        }
        internal static bool SetVelocity(SimulationContext c, Entity ball, Vector3 velocity)
        {
            if (c.Manager.HasComponent<HeldMotion>(ball) || !(c.Physics is IMarblePhysicsCommands commands) || !Finite(velocity) || !Finite(velocity.sqrMagnitude)) return false;
            var shot = c.Manager.GetComponentData<ShotOrigin>(ball);
            velocity = Vector3.ClampMagnitude(velocity, (float)AttributeRuntime.Get(c, ball, GameAttribute.BALL_MAX_SPEED));
            if (!commands.TrySetVelocity(new MarbleKey(shot.RoundId, shot.SpawnSequence), velocity)) return false;
            SetMotion(c, ball, velocity);
            return true;
        }
        private static void SetMotion(SimulationContext c, Entity ball, Vector3 velocity)
        {
            var motion = c.Manager.GetComponentData<Motion3D>(ball); motion.LinearVelocity = velocity;
            c.Manager.SetComponentData(ball, motion);
            var features = c.Manager.GetComponentData<BallFeatures>(ball); features.MovedTick = c.Round.Tick;
            c.Manager.SetComponentData(ball, features);
        }
        internal static void Consume(SimulationContext c, Entity ball, bool refund)
        {
            c.Manager.SetComponentData(ball, new SettlementState { IsSettled = true });
            c.Manager.SetComponentData(ball, new DespawnState { PendingDespawn = true, Reason = MarbleDespawnReason.Drained });
            if (!refund)
            { var features = c.Manager.GetComponentData<BallFeatures>(ball); features.Revive = false; c.Manager.SetComponentData(ball, features); }
        }
        internal static void Refund(SimulationContext c, Entity ball)
        {
            if (!AttributeRuntime.Enabled(c)) return;
            var features = c.Manager.GetComponentData<BallFeatures>(ball);
            if (!features.Revive || !c.RefundedFamilies.Add(features.Family)) return;
            var player = c.Player; player.Blood = Math.Min(c.Tuning.BloodCapacity, player.Blood + features.RefundBlood); c.Player = player;
            var round = c.Round;
            if (!round.CashoutRequested && round.Phase == (byte)RoundPhase.Draining && !round.SettlementComplete)
            { round.Phase = (byte)RoundPhase.Playing; c.Round = round; }
        }
    }
}
