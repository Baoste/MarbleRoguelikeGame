using UnityEngine;

namespace MarblesECS
{
    /// <summary>Optional physics adapter hook for shared per-instance gameplay availability and usage.</summary>
    public interface IMarbleDeviceUsageSink
    {
        void BindDeviceUsage(System.Func<int, bool> available, System.Func<int, bool> tryUse);
        void RefreshDeviceAvailability();
    }
    public struct MarblePhysicalSettings
    {
        public float Radius, Mass, Bounce, Friction, Gravity, MaxSpeed, MagnetResponse, PierceChance, MagneticRadius;
        public int PierceCount;
        public bool Magnetic;
    }
    public struct DevicePose
    {
        public Vector3 Position, Forward, Up;
        public float Radius;
    }
    public interface IMarblePhysicsCommands
    {
        bool TryMove(MarbleKey key, Vector3 position, Vector3 velocity);
        bool TrySetVelocity(MarbleKey key, Vector3 velocity);
        void SetProperties(MarbleKey key, MarblePhysicalSettings settings);
        bool TryGetDevicePose(int instanceId, out DevicePose pose);
        void BreakPinsOnCross(int instanceId);
    }
    /// <summary>Optional adapter capability: suspend all motion/collisions, then restore spin and collision mode.</summary>
    public interface IMarblePhysicsSuspension
    {
        bool TrySetSuspended(MarbleKey key, bool suspended);
    }
}
