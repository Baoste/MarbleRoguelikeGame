using Unity.Entities;
using Unity.Collections;
using System.Collections.Generic;

namespace MarblesECS
{
    internal struct DeviceUseKey : IBufferElementData { internal FixedString128Bytes Key; internal int Round; }
    internal struct DeviceEffectContinuation : IComponentData { internal int Round; internal double Until; }
    internal static class DeviceDurabilitySystem
    {
        internal static int Limit(SimulationContext c, OwnedDeviceData device) => device.HasDurabilityLimit
            ? device.DurabilityLimit : CampaignStateUtility.Device(c, device.DefinitionId).Durability;
        internal static OwnedDeviceData WithLimit(OwnedDeviceData device, int maximum)
        {
            if (maximum < -1 || maximum > 10000) throw new System.ArgumentOutOfRangeException(nameof(maximum));
            device.HasDurabilityLimit = true; device.DurabilityLimit = maximum; return device;
        }
        internal static OwnedDeviceData Restored(SimulationContext c, OwnedDeviceData device)
        { device.DurabilityRound = c.Round.RoundId; device.UsesThisRound = 0; return device; }
        internal static void Refresh(SimulationContext c)
        { if (c.Physics is IMarbleDeviceUsageSink sink) sink.RefreshDeviceAvailability(); }
        internal static void SetLimit(SimulationContext c, Entity entity, int maximum)
        { c.Manager.SetComponentData(entity, WithLimit(c.Manager.GetComponentData<OwnedDeviceData>(entity), maximum)); Refresh(c); }
        internal static void Restore(SimulationContext c, Entity entity)
        { c.Manager.SetComponentData(entity, Restored(c, c.Manager.GetComponentData<OwnedDeviceData>(entity))); Refresh(c); }
        internal static int Remaining(SimulationContext c, OwnedDeviceData device)
        {
            int limit = Limit(c, device);
            if (limit == -1) return -1;
            return System.Math.Max(0, limit - (device.DurabilityRound == c.Round.RoundId ? device.UsesThisRound : 0));
        }
        internal static bool Available(SimulationContext c, int instanceId) =>
            c.TryGetDevice(instanceId, out _, out var device) && Available(c, device);
        internal static bool Available(SimulationContext c, OwnedDeviceData device) => device.Placed && Remaining(c, device) != 0;
        internal static bool Continuing(SimulationContext c, Entity entity) => c.Manager.HasComponent<DeviceEffectContinuation>(entity) &&
            c.Manager.GetComponentData<DeviceEffectContinuation>(entity).Round == c.Round.RoundId &&
            c.Round.Time <= c.Manager.GetComponentData<DeviceEffectContinuation>(entity).Until + 1e-7;
        internal static bool HasPendingEffect(SimulationContext context, Entity entity, int instanceId) =>
            context.IsRunning && (Continuing(context, entity) || HeldMotionSystem.Count(context, instanceId) != 0);
        internal static bool CanRun(SimulationContext c, int id) =>
            c.TryGetDevice(id, out var entity, out var device) && CanRun(c, entity, device);
        internal static bool CanRun(SimulationContext c, Entity entity) =>
            c.IsDeviceEntity(entity) && CanRun(c, entity, c.Manager.GetComponentData<OwnedDeviceData>(entity));
        internal static bool CanRun(SimulationContext c, Entity entity, OwnedDeviceData device) =>
            device.Placed && c.HasProductionFeature(ProductionFeature.DevicePlacement) &&
            (Remaining(c, device) != 0 || Continuing(c, entity));
        internal static bool KeepActive(SimulationContext c, Entity entity, float seconds)
        {
            if (!BallInteractionSystem.Finite(seconds) || seconds <= 0 || seconds > 60 || !c.Manager.Exists(entity) ||
                !c.Manager.GetComponentData<OwnedDeviceData>(entity).Placed) return false;
            var state = new DeviceEffectContinuation { Round = c.Round.RoundId, Until = c.Round.Time + seconds };
            if (c.Manager.HasComponent<DeviceEffectContinuation>(entity)) c.Manager.SetComponentData(entity, state);
            else c.Manager.AddComponentData(entity, state);
            if (c.Physics is IMarbleDeviceUsageSink sink) sink.RefreshDeviceAvailability();
            return true;
        }
        private static bool ValidKey(string key) => !string.IsNullOrEmpty(key) && System.Text.Encoding.UTF8.GetByteCount(key) <= 120;
        private static bool Used(SimulationContext c, Entity entity, string key)
        {
            if (!c.Manager.HasBuffer<DeviceUseKey>(entity)) return false;
            var keys = c.Manager.GetBuffer<DeviceUseKey>(entity);
            var value = new FixedString128Bytes(key);
            foreach (var entry in keys) if (entry.Round == c.Round.RoundId && entry.Key.Equals(value)) return true;
            return false;
        }
        internal static bool CanUseOnce(SimulationContext c, Entity entity, string key) =>
            c.Manager.Exists(entity) && ValidKey(key) && (Used(c, entity, key) || Available(c, c.Manager.GetComponentData<OwnedDeviceData>(entity)));
        internal static bool TryUseOnce(SimulationContext c, Entity entity, string key)
        {
            if (!c.Manager.Exists(entity) || !ValidKey(key)) return false;
            if (Used(c, entity, key)) return true;
            if (!TryUse(c, entity)) return false;
            var keys = c.Manager.HasBuffer<DeviceUseKey>(entity) ? c.Manager.GetBuffer<DeviceUseKey>(entity) : c.Manager.AddBuffer<DeviceUseKey>(entity);
            for (int i = keys.Length - 1; i >= 0; i--) if (keys[i].Round != c.Round.RoundId) keys.RemoveAt(i);
            keys.Add(new DeviceUseKey { Round = c.Round.RoundId, Key = new FixedString128Bytes(key) });
            return true;
        }
        internal static bool TryUse(SimulationContext c, int instanceId) =>
            c.TryGetDevice(instanceId, out var entity, out var device) && TryUse(c, entity, device);
        internal static bool TryUse(SimulationContext c, Entity entity)
        {
            return c.IsDeviceEntity(entity) && TryUse(c, entity, c.Manager.GetComponentData<OwnedDeviceData>(entity));
        }
        private static bool TryUse(SimulationContext c, Entity entity, OwnedDeviceData device)
        {
            if (!Available(c, device)) return false;
            if (Remaining(c, device) == -1) return true;
            if (device.DurabilityRound != c.Round.RoundId) { device.DurabilityRound = c.Round.RoundId; device.UsesThisRound = 0; }
            device.UsesThisRound++;
            c.Manager.SetComponentData(entity, device);
            if (Remaining(c, device) == 0 && c.Physics is IMarbleDeviceUsageSink sink) sink.RefreshDeviceAvailability();
            return true; // The final accepted operation is allowed to finish.
        }

        internal static void RemoveExhausted(SimulationContext context, List<DeviceBrokenEvent> broken)
        {
            if (!context.Campaign.Production.DestroyExhaustedDevices) return;
            using var entities = context.SnapshotDevices();
            foreach (var entity in entities.Items)
            {
                if (!context.IsDeviceEntity(entity)) continue;
                var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
                if (!device.Placed || Remaining(context, device) != 0 ||
                    HasPendingEffect(context, entity, device.InstanceId) ||
                    (device.PairId != 0 && PairHasPendingEffect(context, device.PairId, entities.Items))) continue;
                CampaignStateUtility.DestroyDevice(context, entity);
                broken.Add(new DeviceBrokenEvent(context.Round.RoundId, device.InstanceId, device.DefinitionId));
            }
            if (broken.Count > 1) broken.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
        }

        private static bool PairHasPendingEffect(SimulationContext context, int pairId, List<Entity> devices)
        {
            foreach (var entity in devices)
            {
                if (!context.IsDeviceEntity(entity)) continue;
                var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
                if (device.PairId == pairId && HasPendingEffect(context, entity, device.InstanceId)) return true;
            }
            return false;
        }
    }
}
