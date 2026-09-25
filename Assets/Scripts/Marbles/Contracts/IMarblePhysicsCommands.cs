using UnityEngine;

namespace MarblesECS
{
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
        void SetProperties(MarbleKey key, MarblePhysicalSettings settings);
        bool TryGetDevicePose(int instanceId, out DevicePose pose);
        void FireBloodCannon(int instanceId);
    }
}
