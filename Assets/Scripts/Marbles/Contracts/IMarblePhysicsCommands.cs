using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS
{
    public readonly struct DevicePlacement
    {
        public readonly int InstanceId;
        public readonly DeviceDefinition Definition;
        public readonly float X, Z, Angle;

        public DevicePlacement(int instanceId, DeviceDefinition definition, float x, float z, float angle)
        {
            InstanceId = instanceId;
            Definition = definition;
            X = x;
            Z = z;
            Angle = angle;
        }
    }

    /// <summary>A session's physical placements. A rejected change leaves its previous bodies and poses intact.</summary>
    public interface IMarbleDevicePhysics
    {
        bool CanPlaceDevice(DevicePlacement placement, float clearance, bool avoidMarbles);
        bool TrySetDevicePlacement(DevicePlacement placement, Func<int, uint, DeviceLuaScript> getScript);
        bool TryReplaceDevicePlacements(IReadOnlyList<DevicePlacement> placements, Func<int, uint, DeviceLuaScript> getScript);
        void RemoveDevice(int instanceId);
        void ClearDevices();
    }

    /// <summary>Optional physics adapter hook for shared per-instance gameplay availability and usage.</summary>
    public interface IMarbleDeviceUsageSink
    {
        void BindDeviceUsage(System.Func<int, bool> available, System.Func<int, bool> tryUse);
        void RefreshDeviceAvailability();
    }
    public interface IMarbleDeviceInteractionSink
    {
        void BindDeviceInteraction(System.Func<int, bool> canComplete);
    }
    public interface IMarbleDeviceRotation
    {
        bool TrySetDeviceYaw(int instanceId, float degrees);
    }
    /// <summary>Reports successful per-ball field effects without turning them into collision events.</summary>
    public interface IMarbleDeviceEffectSink
    {
        void BindDeviceEffect(System.Action<MarbleKey, int> recordEffect);
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
    /// <summary>Optional presentation capability. Hidden hold renderers are restored on resume or removal.</summary>
    public interface IMarbleHeldVisibility
    {
        bool TrySetHeldVisible(MarbleKey key, bool visible);
    }
}
