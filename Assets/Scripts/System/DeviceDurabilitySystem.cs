using Unity.Entities;
using Unity.Collections;

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
            CampaignStateUtility.FindDevice(c, instanceId, out var entity) &&
            Available(c, c.Manager.GetComponentData<OwnedDeviceData>(entity));
        internal static bool Available(SimulationContext c, OwnedDeviceData device) => device.Placed && Remaining(c, device) != 0;
        internal static bool Continuing(SimulationContext c, Entity entity) => c.Manager.HasComponent<DeviceEffectContinuation>(entity) &&
            c.Manager.GetComponentData<DeviceEffectContinuation>(entity).Round == c.Round.RoundId &&
            c.Round.Time <= c.Manager.GetComponentData<DeviceEffectContinuation>(entity).Until + 1e-7;
        internal static bool CanRun(SimulationContext c, int id) => CampaignStateUtility.FindDevice(c, id, out var entity) && CanRun(c, entity);
        internal static bool CanRun(SimulationContext c, Entity entity)
        {
            var device = c.Manager.GetComponentData<OwnedDeviceData>(entity);
            return device.Placed && (Remaining(c, device) != 0 || Continuing(c, entity));
        }
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
            CampaignStateUtility.FindDevice(c, instanceId, out var entity) && TryUse(c, entity);
        internal static bool TryUse(SimulationContext c, Entity entity)
        {
            if (!c.Manager.Exists(entity)) return false;
            var device = c.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (!Available(c, device)) return false;
            if (Remaining(c, device) == -1) return true;
            if (device.DurabilityRound != c.Round.RoundId) { device.DurabilityRound = c.Round.RoundId; device.UsesThisRound = 0; }
            device.UsesThisRound++;
            c.Manager.SetComponentData(entity, device);
            if (Remaining(c, device) == 0 && c.Physics is IMarbleDeviceUsageSink sink) sink.RefreshDeviceAvailability();
            return true; // The final accepted operation is allowed to finish.
        }
    }
}
