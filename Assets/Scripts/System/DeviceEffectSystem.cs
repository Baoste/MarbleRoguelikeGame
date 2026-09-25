using System;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal static class DeviceEffectSystem
    {
        internal static void Execute(SimulationContext c, Entity ball, MarbleContact contact)
        {
            var features = c.Manager.GetComponentData<BallFeatures>(ball);
            if (features.MovedTick == c.Round.Tick) return;
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
            int id = contact.TargetId - 100000;
            if (!CampaignStateUtility.FindDevice(c, id, out var deviceEntity)) return;
            var device = c.Manager.GetComponentData<OwnedDeviceData>(deviceEntity);
            if (!device.Placed) return;
            var definition = CampaignStateUtility.Device(c, device.DefinitionId);
            var commands = c.Physics as IMarblePhysicsCommands;
            DevicePose pose = default;
            if (commands == null || !commands.TryGetDevicePose(id, out pose)) return;
            AttributeRuntime.Recompute(c, deviceEntity);
            double rate = AttributeRuntime.Get(c, ball, GameAttribute.DEVICE_TRIGGER_RATE) * AttributeRuntime.Get(c, deviceEntity, GameAttribute.DEVICE_TRIGGER_RATE);
            if (rate <= 0 || device.NextTriggerAt > c.Round.Time) return;
            long visitKey = definition.Kind == DeviceKind.Portal ? -device.PairId : contact.LinkKey != 0 ? contact.LinkKey : contact.TargetId;
            if (definition.Kind == DeviceKind.Amplifier && contact.LinkKey == 0) return;
            if (definition.Kind == DeviceKind.Portal && (device.PairId == 0 || features.PortalLockedUntil > c.Round.Time)) return;
            if (!MarkVisit(c, ball, visitKey, definition.MaxTriggersPerBall)) return;
            device.NextTriggerAt = c.Round.Time + Math.Max(c.StepSeconds,
                AttributeRuntime.Device(c, deviceEntity, GameAttribute.DEVICE_COOLDOWN) / rate);
            c.Manager.SetComponentData(deviceEntity, device);
            double deviceMultiplier = AttributeRuntime.Get(c, deviceEntity, GameAttribute.MACHINE_MULT);
            if (deviceMultiplier != 1) AttributeRuntime.SetBase(c, ball, GameAttribute.MACHINE_MULT,
                AttributeRuntime.Get(c, ball, GameAttribute.MACHINE_MULT) * deviceMultiplier);
            if (features.Replicates) Replicate(c, ball, contact);
            double value = AttributeRuntime.Get(c, ball, GameAttribute.BALL_BASE_VALUE);
            switch (definition.Kind)
            {
                case DeviceKind.Bank:
                    Consume(c, ball, true);
                    var campaign = c.Campaign;
                    campaign.Coins = MarbleRules.AddScore(campaign.Coins,
                        MarbleRules.RoundScore(value * definition.Value * AttributeRuntime.Global(c, GameAttribute.COIN_GAIN_RATE), CampaignStateUtility.MaxCoins), CampaignStateUtility.MaxCoins);
                    c.Campaign = campaign;
                    break;
                case DeviceKind.Amplifier:
                case DeviceKind.Lens:
                    AttributeRuntime.SetBase(c, ball, GameAttribute.BALL_BASE_VALUE, value * definition.Value);
                    break;
                case DeviceKind.Capital:
                    if (value < 6) Consume(c, ball, true);
                    else AttributeRuntime.SetBase(c, ball, GameAttribute.BALL_BASE_VALUE, value * definition.Value);
                    break;
                case DeviceKind.Clover:
                    features.Clover = true; c.Manager.SetComponentData(ball, features); break;
                case DeviceKind.Revive:
                    features.Revive = true; c.Manager.SetComponentData(ball, features); break;
                case DeviceKind.Slow:
                    Move(c, ball, c.Manager.GetComponentData<Transform3D>(ball).Position,
                        c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity * (float)definition.Value);
                    break;
                case DeviceKind.Centrifuge:
                case DeviceKind.Paddle:
                    float speed = c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity.magnitude;
                    Vector3 direction = definition.Kind == DeviceKind.Centrifuge ? Down(pose) : pose.Forward;
                    float outputSpeed = definition.Kind == DeviceKind.Centrifuge ? speed + (float)definition.Value : Math.Max(speed, definition.Strength);
                    Exit(c, ball, pose, direction, outputSpeed); break;
                case DeviceKind.Portal:
                    if (!Partner(c, device, out var partner) || !commands.TryGetDevicePose(partner.InstanceId, out var otherPose)) return;
                    Vector3 output = features.ExitDown ? Down(otherPose) : otherPose.Forward;
                    if (Exit(c, ball, otherPose, output, c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity.magnitude))
                    {
                        features = c.Manager.GetComponentData<BallFeatures>(ball);
                        features.ExitDown = false; features.PortalLockedUntil = c.Round.Time + .5;
                        c.Manager.SetComponentData(ball, features);
                    }
                    break;
                case DeviceKind.Splitter:
                    if (value >= 2)
                    {
                        AttributeRuntime.SetBase(c, ball, GameAttribute.BALL_CLONE_VALUE_RETENTION, .5);
                        ContentSpawnSystem.Children(c, ball, 2, true, Down(pose), pose.Up);
                    }
                    else
                    {
                        var round = c.Round; bool left = MarbleRules.Roll(ref round.RandomState, .5); c.Round = round;
                        Exit(c, ball, pose, Quaternion.AngleAxis(left ? -45 : 45, pose.Up) * Down(pose), 2);
                    }
                    break;
                case DeviceKind.BloodCannon:
                    Consume(c, ball, true);
                    device = c.Manager.GetComponentData<OwnedDeviceData>(deviceEntity);
                    device.StoredValue += value;
                    c.Manager.SetComponentData(deviceEntity, device);
                    if (device.StoredValue >= definition.Value)
                    {
                        commands.FireBloodCannon(id);
                        c.Manager.DestroyEntity(deviceEntity);
                        var state = c.Campaign; state.LayoutRevision++; c.Campaign = state;
                    }
                    break;
            }
        }

        private static bool MarkVisit(SimulationContext c, Entity ball, long key, int maximum)
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
        private static bool Partner(SimulationContext c, OwnedDeviceData source, out OwnedDeviceData partner)
        {
            using (var entities = c.DeviceQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var entity in entities)
                {
                    partner = c.Manager.GetComponentData<OwnedDeviceData>(entity);
                    if (partner.Placed && partner.InstanceId != source.InstanceId && partner.PairId == source.PairId) return true;
                }
            partner = default; return false;
        }
        private static Vector3 Down(DevicePose pose)
        {
            var down = Vector3.ProjectOnPlane(Vector3.down, pose.Up);
            return down.sqrMagnitude > .0001f ? down.normalized : pose.Forward;
        }
        private static Vector3 Direction(SimulationContext c, Entity ball)
        {
            var velocity = c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity;
            return velocity.sqrMagnitude > .0001f ? velocity.normalized : Vector3.down;
        }
        private static void Replicate(SimulationContext c, Entity ball, MarbleContact contact)
        {
            Vector3 normal = Vector3.up;
            if (c.Physics is IMarblePhysicsCommands commands && commands.TryGetDevicePose(contact.TargetId - 100000, out var pose)) normal = pose.Up;
            ContentSpawnSystem.Children(c, ball, 1, false, Direction(c, ball), normal);
        }
        private static bool Exit(SimulationContext c, Entity ball, DevicePose pose, Vector3 direction, float speed)
        {
            float radius = (float)AttributeRuntime.Get(c, ball, GameAttribute.BALL_RADIUS) * AttributeRuntime.RadiusToWorld;
            return Move(c, ball, pose.Position + pose.Up * (radius + .005f) + direction.normalized * (pose.Radius + radius + .02f), direction.normalized * Math.Max(.1f, speed));
        }
        private static bool Move(SimulationContext c, Entity ball, Vector3 position, Vector3 velocity)
        {
            if (!(c.Physics is IMarblePhysicsCommands commands)) return false;
            var shot = c.Manager.GetComponentData<ShotOrigin>(ball);
            velocity = Vector3.ClampMagnitude(velocity, (float)AttributeRuntime.Get(c, ball, GameAttribute.BALL_MAX_SPEED));
            if (!commands.TryMove(new MarbleKey(shot.RoundId, shot.SpawnSequence), position, velocity)) return false;
            var pose = c.Manager.GetComponentData<Transform3D>(ball); pose.Position = position;
            c.Manager.SetComponentData(ball, pose);
            c.Manager.SetComponentData(ball, new Motion3D { LinearVelocity = velocity });
            var features = c.Manager.GetComponentData<BallFeatures>(ball); features.MovedTick = c.Round.Tick;
            c.Manager.SetComponentData(ball, features); return true;
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
