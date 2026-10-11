using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Composable motion commands for the current device callback. Coordinates are world space.</summary>
    [Preserve]
    public sealed class DeviceMotionApi
    {
        private readonly DeviceContactContext owner;
        private readonly Entity ball, device;
        private readonly DeviceEndpoint source;
        internal DeviceMotionApi(DeviceContactContext owner, Entity ball, Entity device, int instanceId, DevicePose pose)
        {
            this.owner = owner; this.ball = ball; this.device = device;
            source = new DeviceEndpoint(this, instanceId, pose);
        }
        internal SimulationContext Context => owner.Context;
        public Vector3 Position => Context.Manager.GetComponentData<Transform3D>(ball).Position;
        public Vector3 Velocity => Context.Manager.GetComponentData<Motion3D>(ball).LinearVelocity;
        public float Speed => Velocity.magnitude;
        public DeviceEndpoint Source { get { _ = Context; return source; } }

        /// <summary>Only placed devices in this simulation can be destinations.</summary>
        public DeviceEndpoint FindDevice(int instanceId)
        {
            var c = Context;
            if (!CampaignStateUtility.FindDevice(c, instanceId, out var entity) ||
                !DeviceDurabilitySystem.Available(c, c.Manager.GetComponentData<OwnedDeviceData>(entity)) ||
                !(c.Physics is IMarblePhysicsCommands commands) || !commands.TryGetDevicePose(instanceId, out var pose)) return null;
            return new DeviceEndpoint(this, instanceId, pose);
        }
        public DeviceEndpoint FindPartner()
        {
            var c = Context;
            var current = c.Manager.GetComponentData<OwnedDeviceData>(device);
            if (current.PairId == 0) return null;
            DeviceEndpoint result = null;
            using (var entities = c.DeviceQuery.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    var other = c.Manager.GetComponentData<OwnedDeviceData>(entity);
                    if (other.InstanceId == current.InstanceId || other.PairId != current.PairId || !other.Placed ||
                        (result != null && other.InstanceId >= result.InstanceId)) continue;
                    var endpoint = FindDevice(other.InstanceId);
                    if (endpoint != null) result = endpoint;
                }
            return result;
        }
        /// <summary>Preserves linear speed (including zero) and uses the destination's facing.</summary>
        public bool TryTeleport(DeviceEndpoint destination)
        {
            _ = Context;
            return Owns(destination) && TryLaunch(destination, destination.Forward, Speed);
        }
        /// <summary>Places the ball outside the device and emits it along a world-space direction.</summary>
        public bool TryLaunch(DeviceEndpoint destination, Vector3 direction, float speed)
        {
            var c = Context;
            return Owns(destination) && BallInteractionSystem.Exit(c, ball, destination.Pose, direction, speed);
        }
        public bool TryMove(Vector3 position, Vector3 velocity) => BallInteractionSystem.Move(Context, ball, position, velocity);
        /// <summary>Changes only world position, with clearance checks, retaining the current velocity and spin.</summary>
        public bool TrySetPosition(Vector3 position) => TryMove(position, Velocity);
        public bool TrySetVelocity(Vector3 velocity) => BallInteractionSystem.SetVelocity(Context, ball, velocity);
        public bool TryScaleSpeed(float scale)
        {
            _ = Context;
            return BallInteractionSystem.Finite(scale) && scale >= 0 && TrySetVelocity(Velocity * scale);
        }
        /// <summary>Suspends in place for (0, 60] simulation seconds, restores motion, then invokes OnHoldEnd.</summary>
        public bool TryHold(float seconds) => HeldMotionSystem.Begin(Context, ball, source.InstanceId, owner.Contact, seconds);
        private bool Owns(DeviceEndpoint endpoint) => endpoint != null && ReferenceEquals(endpoint.Owner, this);
    }

    /// <summary>Read-only device pose, valid only during the originating device callback.</summary>
    [Preserve]
    public sealed class DeviceEndpoint
    {
        internal readonly DeviceMotionApi Owner;
        private readonly int instanceId;
        private readonly DevicePose pose;
        internal DeviceEndpoint(DeviceMotionApi owner, int instanceId, DevicePose pose)
        { Owner = owner; this.instanceId = instanceId; this.pose = pose; }
        internal DevicePose Pose { get { _ = Owner.Context; return pose; } }
        public int InstanceId { get { _ = Owner.Context; return instanceId; } }
        public Vector3 Position => Pose.Position;
        public Vector3 Forward => Pose.Forward.normalized;
        public Vector3 Up => Pose.Up.normalized;
        public float Radius => Pose.Radius;
        public Vector3 Down => BallInteractionSystem.Down(Pose).normalized;
        /// <summary>Rotates a world-space direction around the device's Up axis, in degrees.</summary>
        public Vector3 Rotate(Vector3 direction, float degrees) => Quaternion.AngleAxis(degrees, Up) * direction;
    }
}
