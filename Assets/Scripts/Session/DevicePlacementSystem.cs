using Unity.Collections;

namespace MarblesECS
{
    internal static class DevicePlacementSystem
    {
        internal static bool Place(SimulationContext context, int instanceId, float x, float z)
        {
            if (!MarbleRules.IsFinite(x) || !MarbleRules.IsFinite(z) ||
                !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            var definition = CampaignStateUtility.Device(context, device.DefinitionId);
            var board = context.Balance.Board;
            float radius = definition.Radius;
            if (x - radius < board.PlacementMinX || x + radius > board.PlacementMaxX ||
                z - radius < board.PlacementMinZ || z + radius > board.PlacementMaxZ) return false;
            if (OverlapsFixedPin(board, x, z, radius)) return false;
            using (var entities = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                {
                    var other = context.Manager.GetComponentData<OwnedDeviceData>(entities[i]);
                    if (!other.Placed || other.InstanceId == instanceId) continue;
                    float distance = radius + CampaignStateUtility.Device(context, other.DefinitionId).Radius + board.PlacementClearance;
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

        private static bool OverlapsFixedPin(BoardTuning board, float x, float z, float radius)
        {
            float distance = radius + board.FixedPinRadius + board.PlacementClearance;
            for (int row = 0; row < board.FixedPinRows; row++)
            {
                int columns = board.FixedPinColumns - row % 2;
                float pinZ = board.FixedPinStartZ - row * board.FixedPinSpacingZ;
                for (int col = 0; col < columns; col++)
                {
                    float pinX = (col - (columns - 1) * 0.5f) * board.FixedPinSpacingX;
                    if (Overlaps(x, z, pinX, pinZ, distance)) return true;
                }
            }
            return false;
        }

        private static bool Overlaps(float x, float z, float otherX, float otherZ, float distance)
        {
            double dx = x - otherX;
            double dz = z - otherZ;
            return dx * dx + dz * dz < distance * distance;
        }
    }
}
