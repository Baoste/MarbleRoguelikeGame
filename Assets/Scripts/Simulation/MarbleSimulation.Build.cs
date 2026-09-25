namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Checks device spacing after the scene has accepted the placement plane and obstacle checks.</summary>
        public bool PlaceDevice(int instanceId, float x, float z, float clearance = 0.15f)
        {
            return CanCampaignAct(RoundPhase.Build) && DevicePlacementSystem.Place(context, instanceId, x, z, clearance);
        }

        public bool RemoveDevicePlacement(int instanceId)
        {
            if (!CanCampaignAct(RoundPhase.Build) ||
                !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (!device.Placed) return false;
            device.Placed = false;
            context.Manager.SetComponentData(entity, device);
            var campaign = context.Campaign;
            campaign.LayoutRevision++;
            context.Campaign = campaign;
            return true;
        }
        public bool RotateDevice(int instanceId, float degrees)
        {
            if (!CanCampaignAct(RoundPhase.Build) || !MarbleRules.IsFinite(degrees) ||
                !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            device.Angle = (device.Angle + degrees) % 360; context.Manager.SetComponentData(entity, device);
            var campaign = context.Campaign; campaign.LayoutRevision++; context.Campaign = campaign; return true;
        }
    }
}
