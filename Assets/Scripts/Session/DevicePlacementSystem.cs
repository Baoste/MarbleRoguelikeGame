using Unity.Collections;

namespace MarblesECS
{
    internal static class DevicePlacementSystem
    {
        internal static bool Place(SimulationContext context, int instanceId, float x, float z, float clearance)
        {
            if (!MarbleRules.IsFinite(x) || !MarbleRules.IsFinite(z) ||
                !MarbleRules.IsFinite(clearance) || clearance < 0 ||
                !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            var definition = CampaignStateUtility.Device(context, device.DefinitionId);
            float radius = definition.Radius;
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                {
                    var other = context.Manager.GetComponentData<OwnedDeviceData>(entities[i]);
                    if (!other.Placed || other.InstanceId == instanceId) continue;
                    float distance = radius + CampaignStateUtility.Device(context, other.DefinitionId).Radius +
                        clearance;
                    if (definition.Kind == DeviceKind.Amplifier && CampaignStateUtility.Device(context, other.DefinitionId).Kind == DeviceKind.Amplifier)
                        distance = radius + CampaignStateUtility.Device(context, other.DefinitionId).Radius + .01f;
                    if (Overlaps(x, z, other.X, other.Z, distance)) return false;
                }
            device.X = x;
            device.Z = z;
            device.Placed = true;
            context.Manager.SetComponentData(entity, device);
            var campaign = context.Campaign;
            campaign.LayoutRevision++;
            context.Campaign = campaign;
            return true;
        }

        private static bool Overlaps(float x, float z, float otherX, float otherZ, float distance)
        {
            double dx = x - otherX;
            double dz = z - otherZ;
            return dx * dx + dz * dz < distance * distance;
        }
    }
}
