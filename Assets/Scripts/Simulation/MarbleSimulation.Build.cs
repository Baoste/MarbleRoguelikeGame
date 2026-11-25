namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        private bool CanEditDevices => CanCampaignAct(RoundPhase.Build) &&
            context.HasProductionFeature(ProductionFeature.DevicePlacement);

        internal bool CanPlaceDevice(int instanceId, float x, float z, float clearance, float? angle)
        {
            return CanEditDevices && DevicePlacementSystem.CanPlace(context, instanceId, x, z, clearance, angle);
        }

        /// <summary>Checks device spacing after the scene has accepted the placement plane and obstacle checks.</summary>
        public bool PlaceDevice(int instanceId, float x, float z, float clearance = 0.15f, float? angle = null)
        {
            return CanEditDevices && DevicePlacementSystem.Place(context, instanceId, x, z, clearance, angle);
        }

        public bool RemoveDevicePlacement(int instanceId) =>
            CanEditDevices && DevicePlacementSystem.ReturnToInventory(context, instanceId);
        public bool RotateDevice(int instanceId, float degrees)
        {
            if (!CanEditDevices || !MarbleRules.IsFinite(degrees) ||
                !CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (CampaignStateUtility.Device(context, device.DefinitionId).RotationLocked) return false;
            device.Angle = (device.Angle + degrees) % 360; context.Manager.SetComponentData(entity, device);
            var campaign = context.Campaign; campaign.LayoutRevision++; context.Campaign = campaign; return true;
        }
    }
}
