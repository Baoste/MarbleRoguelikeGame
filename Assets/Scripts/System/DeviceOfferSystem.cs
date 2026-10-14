using MRes;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal static class DeviceOfferSystem
    {
        private static bool Resolve(SimulationContext c, int offeredId, int targetId, out Entity offered, out Entity target)
        {
            offered = target = Entity.Null;
            return AttributeRuntime.Enabled(c) && c.Round.Phase == (byte)RoundPhase.Build && offeredId != targetId &&
                CampaignStateUtility.FindDevice(c, offeredId, out offered) && CampaignStateUtility.FindDevice(c, targetId, out target) &&
                !c.Manager.GetComponentData<OwnedDeviceData>(offered).Placed && c.Manager.GetComponentData<OwnedDeviceData>(target).Placed;
        }
        internal static bool CanOffer(SimulationContext c, int offeredId, int targetId)
        {
            if (!Resolve(c, offeredId, targetId, out _, out var target)) return false;
            var device = c.Manager.GetComponentData<OwnedDeviceData>(target);
            return c.DeviceScripts.Get(targetId, device.DefinitionId).HasFunction("OnOffer");
        }
        internal static bool Offer(SimulationContext c, int offeredId, int targetId)
        {
            if (!CanOffer(c, offeredId, targetId) || !Resolve(c, offeredId, targetId, out var offered, out var target)) return false;
            var device = c.Manager.GetComponentData<OwnedDeviceData>(target);
            var api = new DeviceOfferContext(c, target, offered);
            try
            {
                c.DeviceScripts.Get(targetId, device.DefinitionId).Invoke("OnOffer", api);
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
            catch (MResLuaException error) { Debug.LogException(error); return false; }
            finally { api.Close(); }
        }
    }
}
