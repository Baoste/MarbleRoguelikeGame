using Unity.Entities;

namespace MarblesECS
{
    internal static class DeviceDurabilitySystem
    {
        internal static int Remaining(SimulationContext c, OwnedDeviceData device)
        {
            var definition = CampaignStateUtility.Device(c, device.DefinitionId);
            if (definition.Durability == -1) return -1;
            return System.Math.Max(0, definition.Durability - (device.DurabilityRound == c.Round.RoundId ? device.UsesThisRound : 0));
        }
        internal static bool Available(SimulationContext c, int instanceId) =>
            CampaignStateUtility.FindDevice(c, instanceId, out var entity) &&
            Available(c, c.Manager.GetComponentData<OwnedDeviceData>(entity));
        internal static bool Available(SimulationContext c, OwnedDeviceData device) => device.Placed && Remaining(c, device) != 0;
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
