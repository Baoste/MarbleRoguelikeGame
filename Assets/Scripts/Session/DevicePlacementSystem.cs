using Unity.Collections;

namespace MarblesECS
{
    internal static class DevicePlacementSystem
    {
        internal static bool Place(SimulationContext context, int instanceId, float x, float z, float clearance, float? angle = null)
        {
            if (!MarbleRules.IsFinite(x) || !MarbleRules.IsFinite(z) ||
                !MarbleRules.IsFinite(clearance) || clearance < 0 ||
                (angle.HasValue && !MarbleRules.IsFinite(angle.Value)) ||
                !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            var definition = CampaignStateUtility.Device(context, device.DefinitionId);
            float radius = definition.PlacementRadius;
            if (radius <= 0) return false;
            definition.PlacementCenter(x, z, angle ?? device.Angle, out float centerX, out float centerZ);
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                {
                    var other = context.Manager.GetComponentData<OwnedDeviceData>(entities[i]);
                    if (!other.Placed || other.InstanceId == instanceId) continue;
                    var otherDefinition = CampaignStateUtility.Device(context, other.DefinitionId);
                    if (otherDefinition.PlacementRadius <= 0) return false;
                    float distance = radius + otherDefinition.PlacementRadius + clearance;
                    if (!string.IsNullOrEmpty(definition.LinkGroup) && otherDefinition.LinkGroup == definition.LinkGroup)
                        distance = radius + otherDefinition.PlacementRadius + definition.GroupClearance;
                    otherDefinition.PlacementCenter(other.X, other.Z, other.Angle, out float otherX, out float otherZ);
                    if (Overlaps(centerX, centerZ, otherX, otherZ, distance)) return false;
                }
            device.X = x;
            device.Z = z;
            device.Placed = true;
            if (angle.HasValue) device.Angle = angle.Value % 360;
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
