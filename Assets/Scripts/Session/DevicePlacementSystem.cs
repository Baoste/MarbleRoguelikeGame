using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    internal static class DevicePlacementSystem
    {
        internal static bool CanPlace(SimulationContext context, int instanceId, float x, float z, float clearance, float? angle)
            => TryValidatePlacement(context, instanceId, x, z, clearance, angle, out _, out _);

        internal static bool Place(SimulationContext context, int instanceId, float x, float z, float clearance, float? angle = null)
        {
            if (!TryValidatePlacement(context, instanceId, x, z, clearance, angle, out var entity, out var device)) return false;
            device.X = x;
            device.Z = z;
            device.Placed = true;
            return TryCommitPlacement(context, entity, device);
        }

        internal static bool TryCommitPlacement(SimulationContext context, Entity entity, OwnedDeviceData device)
        {
            var definition = CampaignStateUtility.Device(context, device.DefinitionId);
            if (!TrySetPhysicsPlacement(context, device.InstanceId, definition, device)) return false;
            context.Manager.SetComponentData(entity, device);
            DeviceDurabilitySystem.Refresh(context);
            MarkLayoutChanged(context);
            return true;
        }

        internal static bool TrySetPhysicsPlacement(SimulationContext context, int id, DeviceDefinition definition, OwnedDeviceData device)
        {
            return context.Physics is IMarbleDevicePhysics physics &&
                physics.TrySetDevicePlacement(new DevicePlacement(id, definition, device.X, device.Z, device.Angle),
                    context.DeviceScripts.Get);
        }

        internal static bool Rotate(SimulationContext context, int id, float degrees, float clearance)
        {
            if (!context.TryGetDevice(id, out var entity, out var device)) return false;
            var definition = CampaignStateUtility.Device(context, device.DefinitionId);
            if (definition.RotationLocked) return false;
            float angle = (device.Angle + degrees) % 360;
            if (device.Placed) return Place(context, id, device.X, device.Z, clearance, angle);
            device.Angle = angle;
            context.Manager.SetComponentData(entity, device);
            MarkLayoutChanged(context);
            return true;
        }

        internal static bool ReturnToInventory(SimulationContext context, int instanceId)
        {
            if (!CampaignStateUtility.FindDevice(context, instanceId, out var entity) ||
                !ClearPlacement(context, entity)) return false;
            MarkLayoutChanged(context);
            return true;
        }

        internal static void ReturnAllToInventory(SimulationContext context)
        {
            bool layoutChanged = false;
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                    layoutChanged |= ClearPlacement(context, entity);
            if (layoutChanged) MarkLayoutChanged(context);
        }

        private static bool ClearPlacement(SimulationContext context, Entity entity)
        {
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (!device.Placed) return false;
            RemovePhysicsPlacement(context, device.InstanceId);
            device.Placed = false;
            context.Manager.SetComponentData(entity, device);
            return true;
        }

        internal static void RemovePhysicsPlacement(SimulationContext context, int id)
        {
            (context.Physics as IMarbleDevicePhysics)?.RemoveDevice(id);
            context.Transients.RemoveSource(id);
        }

        internal static void MarkLayoutChanged(SimulationContext context)
        {
            var campaign = context.Campaign;
            campaign.LayoutRevision++;
            context.Campaign = campaign;
        }

        private static bool TryValidatePlacement(SimulationContext context, int instanceId, float x, float z, float clearance,
            float? angle, out Entity entity, out OwnedDeviceData device)
        {
            if (!context.TryGetDevice(instanceId, out entity, out device)) return false;
            var definition = CampaignStateUtility.Device(context, device.DefinitionId);
            float proposed = angle ?? device.Angle;
            if (!CanPlaceDefinition(context, definition, x, z, clearance, proposed, instanceId)) return false;
            device.Angle = definition.ResolvePlacementAngle(proposed % 360);
            return true;
        }

        internal static bool CanPlaceDefinition(SimulationContext context, DeviceDefinition definition, float x, float z,
            float clearance, float angle, int excludedInstanceId = 0)
        {
            if (!(context.Physics is IMarbleDevicePhysics physics) ||
                !MarbleRules.IsFinite(x) || !MarbleRules.IsFinite(z) || !MarbleRules.IsFinite(angle) ||
                !MarbleRules.IsFinite(clearance) || clearance < 0) return false;
            float radius = definition.PlacementRadius;
            if (radius <= 0 || (definition.RotationLocked && angle % 360 != 0)) return false;
            definition.PlacementCenter(x, z, angle % 360, out float centerX, out float centerZ);
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                {
                    var other = context.Manager.GetComponentData<OwnedDeviceData>(entities[i]);
                    if (!other.Placed || other.InstanceId == excludedInstanceId) continue;
                    var otherDefinition = CampaignStateUtility.Device(context, other.DefinitionId);
                    if (otherDefinition.PlacementRadius <= 0) return false;
                    float distance = radius + otherDefinition.PlacementRadius + clearance;
                    if (!string.IsNullOrEmpty(definition.LinkGroup) && otherDefinition.LinkGroup == definition.LinkGroup)
                        distance = radius + otherDefinition.PlacementRadius + definition.GroupClearance;
                    otherDefinition.PlacementCenter(other.X, other.Z, other.Angle, out float otherX, out float otherZ);
                    if (Overlaps(centerX, centerZ, otherX, otherZ, distance)) return false;
                }
            return physics.CanPlaceDevice(new DevicePlacement(excludedInstanceId, definition, x, z,
                definition.ResolvePlacementAngle(angle % 360)), clearance, context.IsRunning);
        }

        private static bool Overlaps(float x, float z, float otherX, float otherZ, float distance)
        {
            double dx = x - otherX;
            double dz = z - otherZ;
            return dx * dx + dz * dz < distance * distance;
        }
    }
}
