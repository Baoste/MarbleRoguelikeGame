namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public bool CanOfferDevice(int offeredId, int targetId) =>
            CanCampaignAct(RoundPhase.Build) && DeviceOfferSystem.CanOffer(context, offeredId, targetId);
        public bool TryOfferDevice(int offeredId, int targetId) =>
            CanCampaignAct(RoundPhase.Build) && DeviceOfferSystem.Offer(context, offeredId, targetId);
        /// <summary>Returns whether a placed device's callback ran; Lua decides whether its effect can start.</summary>
        public bool TryActivateDevice(int instanceId)
        {
            ThrowIfDisposed();
            return started && !insidePhysicsStep && DeviceScriptSystem.DeviceEvent(context, instanceId, "OnActivate");
        }
        public string GetDeviceStatus(int instanceId)
        {
            ThrowIfDisposed();
            if (context.Round.Phase == (byte)RoundPhase.Build)
                return CampaignStateUtility.FindDevice(context, instanceId, out var entity) &&
                    context.Manager.GetComponentData<OwnedDeviceData>(entity).Placed
                    ? context.DeviceScripts.Status(instanceId, DeviceRoundState.Round(context)) : string.Empty;
            bool dormant = CampaignStateUtility.FindDevice(context, instanceId, out var dormantEntity) &&
                context.Manager.GetComponentData<OwnedDeviceData>(dormantEntity).Placed &&
                DeviceDurabilitySystem.Limit(context, context.Manager.GetComponentData<OwnedDeviceData>(dormantEntity)) == 0;
            if (!dormant && !DeviceDurabilitySystem.Available(context, instanceId) && !DeviceScriptSystem.CanComplete(context, instanceId)) return string.Empty;
            return context.DeviceScripts.Status(instanceId, context.Round.RoundId);
        }
    }
}
