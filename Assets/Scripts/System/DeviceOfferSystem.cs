using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal static class DeviceOfferSystem
    {
        private static bool Resolve(SimulationContext c, int offeredId, int targetId,
            out Entity offered, out Entity target, out DeviceLuaScript script)
        {
            offered = target = Entity.Null;
            script = null;
            if (!AttributeRuntime.Enabled(c) || c.Round.Phase != (byte)RoundPhase.Build || offeredId == targetId ||
                !CampaignStateUtility.FindDevice(c, offeredId, out offered) ||
                !CampaignStateUtility.FindDevice(c, targetId, out target)) return false;
            var receiver = c.Manager.GetComponentData<OwnedDeviceData>(target);
            var source = c.Manager.GetComponentData<OwnedDeviceData>(offered);
            if (!receiver.Placed || (source.Placed &&
                CampaignStateUtility.Device(c, receiver.DefinitionId)?.AcceptsPlacedOffers != true)) return false;
            script = c.DeviceScripts.Get(targetId, receiver.DefinitionId);
            return script.HasFunction("OnOffer");
        }
        internal static bool CanOffer(SimulationContext c, int offeredId, int targetId) =>
            Resolve(c, offeredId, targetId, out _, out _, out _);
        internal static bool Offer(SimulationContext c, int offeredId, int targetId)
        {
            if (!Resolve(c, offeredId, targetId, out var offered, out var target, out var script)) return false;
            var api = new DeviceOfferContext(c, target, offered);
            try
            {
                script.Invoke("OnOffer", api);
                if (!api.Accepted) return false;
                api.Commit(target);
                c.Manager.DestroyEntity(offered);
                c.DeviceScripts.Remove(offeredId);
                c.Transients.RemoveSource(c, offeredId);
                var campaign = c.Campaign; campaign.LayoutRevision++; c.Campaign = campaign;
                DeviceDurabilitySystem.Refresh(c);
                DeviceScriptSystem.InstanceEvent(c, targetId, "OnOfferAccepted");
                return true;
            }
            catch (DeviceLuaException error) { Debug.LogException(error); return false; }
            finally { api.Close(); }
        }
    }
}
