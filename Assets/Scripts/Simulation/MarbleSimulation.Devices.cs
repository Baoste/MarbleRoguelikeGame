namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Returns whether a placed device's callback ran; Lua decides whether its effect can start.</summary>
        public bool TryActivateDevice(int instanceId)
        {
            ThrowIfDisposed();
            return started && !insidePhysicsStep && DeviceScriptSystem.DeviceEvent(context, instanceId, "OnActivate");
        }
        public string GetDeviceStatus(int instanceId)
        {
            ThrowIfDisposed();
            if (!DeviceDurabilitySystem.Available(context, instanceId) && !DeviceScriptSystem.CanComplete(context, instanceId)) return string.Empty;
            return context.DeviceScripts.Status(instanceId, context.Round.RoundId);
        }
    }
}
