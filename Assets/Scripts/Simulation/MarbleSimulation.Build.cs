namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Stores a placement accepted by the presentation's Inspector-configured placement plane.</summary>
        public bool PlaceDevice(int instanceId, float x, float z)
        {
            return CanCampaignAct(RoundPhase.Build) && DevicePlacementSystem.Place(context, instanceId, x, z);
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
    }
}
