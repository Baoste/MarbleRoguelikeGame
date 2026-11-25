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
            if (!receiver.Placed || (source.PairId != 0 && source.PairId == receiver.PairId) || (source.Placed &&
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
            using (c.DeviceScripts.BeginCallback())
            {
                try
                {
                    script.Invoke("OnOffer", api);
                    if (!api.Accepted || !c.Manager.Exists(offered) || !c.Manager.Exists(target)) return false;
                    api.Commit(target);
                }
                catch (DeviceLuaException error) { Debug.LogException(error); return false; }
                finally { api.Close(); }
            }
            CampaignStateUtility.DestroyDevice(c, offered);
            DeviceScriptSystem.InstanceEvent(c, targetId, "OnOfferAccepted");
            return true;
        }
    }
}
