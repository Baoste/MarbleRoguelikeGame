using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Device-only callback: no ball is required. Valid for one tick, activation or transient completion.</summary>
    [Preserve]
    public sealed class DeviceEventContext
    {
        private SimulationContext c;
        private readonly int instanceId;
        private readonly Entity deviceEntity;
        private bool usageGranted;
        private readonly DeviceDefinition definition;
        internal DeviceEventContext(SimulationContext context, Entity device, DeviceTransientSystem.Item ended = null)
        {
            c = context;
            deviceEntity = device;
            var owned = c.Manager.GetComponentData<OwnedDeviceData>(device);
            instanceId = owned.InstanceId;
            definition = CampaignStateUtility.Device(c, owned.DefinitionId);
            Vectors = new DeviceVectorApi(() => C);
            Board = new DeviceBoardApi(() => C, instanceId);
            if (ended != null) transient = new DeviceTransientInfo(() => C, ended.Id,
                ended.Origin + ended.Velocity * ended.Duration, ended.Velocity, ended.Duration);
        }
        private SimulationContext C => c ?? throw new InvalidOperationException("The Lua device callback has ended.");
        internal void Close() { c = null; }
        private readonly DeviceTransientInfo transient;
        public DeviceTransientInfo Transient { get { _ = C; return transient; } }
        public int TrySpawnVisual(string nodePath, Vector3 position, Vector3 velocity, float seconds, string callback)
            => C.Transients.Spawn(C, instanceId, nodePath, position, velocity, seconds, callback);
        public int ForEachBallInRadius(Vector3 center, float radius, string callback)
            => DeviceScriptSystem.ForEachBallInRadius(C, instanceId, center, radius, callback);
        public double RandomRange(double minimum, double maximum)
        {
            var context = C;
            if (!MarbleRules.IsFinite(minimum) || !MarbleRules.IsFinite(maximum) || minimum > maximum ||
                !MarbleRules.IsFinite(maximum - minimum)) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (minimum == maximum) return minimum;
            var round = context.Round;
            double result = minimum + (maximum - minimum) * MarbleRules.NextRandom(ref round.RandomState);
            context.Round = round; return result;
        }
        public int InstanceId { get { _ = C; return instanceId; } }
        public uint DefinitionId { get { _ = C; return definition.Id; } }
        public int CountPlacedDevices(int excludedInstanceId = 0) => CampaignStateUtility.CountPlacedDevices(C, excludedInstanceId);
        public int CountPlacedDevicesWithRoundValue(uint definitionId, string key, double value)
            => DeviceRoundState.CountPlaced(C, definitionId, key, value);
        public int CountPlacedDevicesOfDefinition(uint definitionId) => CampaignStateUtility.CountPlacedDevicesOfDefinition(C, definitionId);
        public int ForEachPlacedDevice(uint definitionId, string callback, double value)
            => DeviceScriptSystem.ForEachPlacedDevice(C, InstanceId, definitionId, callback, value);
        public int RemainingDurability => CampaignStateUtility.FindDevice(C, instanceId, out var entity)
            ? DeviceDurabilitySystem.Remaining(C, C.Manager.GetComponentData<OwnedDeviceData>(entity)) : 0;
        public int RoundId => C.Round.RoundId;
        public double GetRoundValue(string key) => DeviceRoundState.Get(C, deviceEntity, key);
        public void SetRoundValue(string key, double value) => DeviceRoundState.Set(C, deviceEntity, key, value);
        public double GetInstanceValue(string key) => DeviceInstanceState.Get(C, deviceEntity, key);
        public void SetInstanceValue(string key, double value) => DeviceInstanceState.Set(C, deviceEntity, key, value);
        public int DurabilityLimit => DeviceDurabilitySystem.Limit(C, C.Manager.GetComponentData<OwnedDeviceData>(deviceEntity));
        public void SetDurabilityLimit(int maximum) => DeviceDurabilitySystem.SetLimit(C, deviceEntity, maximum);
        public void RestoreDurability() => DeviceDurabilitySystem.Restore(C, deviceEntity);
        /// <summary>Current wallet balance, readable without an associated ball.</summary>
        public double Coins => C.Campaign.Coins;
        public double Blood => C.Player.Blood;
        public double BloodCapacity => C.Tuning.BloodCapacity;
        public double RestoreBlood(double amount) => PlayerBloodSystem.Restore(C, amount);
        public double Time => C.Round.Time;
        public float DeltaTime => C.StepSeconds;
        public double Parameter { get { _ = C; return definition.Value; } }
        public double ScoreMultiplier { get { _ = C; return definition.ScoreMultiplier; } }
        public float Strength { get { _ = C; return definition.Strength; } }
        public float Range { get { _ = C; return definition.Range; } }
        public int HeldCount => HeldMotionSystem.Count(C, instanceId);
        public DeviceVectorApi Vectors { get; }
        public DeviceBoardApi Board { get; }
        public DeviceSpawnBatch CreateSpawnBatch() { _ = C; return new DeviceSpawnBatch(() => C, instanceId); }
        public bool TryUse() { _ = C; return usageGranted || (usageGranted = DeviceDurabilitySystem.TryUse(C, deviceEntity)); }
        public bool CanUseOnce(string key) => DeviceDurabilitySystem.CanUseOnce(C, deviceEntity, key);
        public bool TryUseOnce(string key) => DeviceDurabilitySystem.TryUseOnce(C, deviceEntity, key);
        public bool TryKeepActive(float seconds) => DeviceDurabilitySystem.KeepActive(C, deviceEntity, seconds);
        /// <summary>Completes a snapshot of this device's holds, invoking fresh OnHoldEnd contexts in spawn order.</summary>
        public int ReleaseHeld() => HeldMotionSystem.Release(C, instanceId);
        private DevicePose Pose => C.Physics is IMarblePhysicsCommands commands && commands.TryGetDevicePose(instanceId, out var pose)
            ? pose : throw new InvalidOperationException("The device has no placed physical view.");
        public Vector3 Forward => Pose.Forward.normalized;
        public Vector3 Down => BallInteractionSystem.Down(Pose).normalized;
        public Vector3 Up => Pose.Up.normalized;
        public Vector3 Position => Pose.Position;
        public float Radius => Pose.Radius;
        /// <summary>Absolute yaw offset relative to the placed pose; does not edit the inventory layout.</summary>
        public bool TrySetYaw(float degrees)
        {
            var context = C;
            return BallInteractionSystem.Finite(degrees) && context.Physics is IMarbleDeviceRotation rotation && rotation.TrySetDeviceYaw(instanceId, degrees);
        }
        public void SetStatus(string text) => C.DeviceScripts.SetStatus(instanceId, DeviceRoundState.Round(C), text);
    }
}
