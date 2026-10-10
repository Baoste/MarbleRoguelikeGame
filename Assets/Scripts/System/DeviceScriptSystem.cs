using System.Collections.Generic;
using MRes;
using Unity.Entities;

namespace MarblesECS
{
    internal sealed class DeviceScriptSystem
    {
        private readonly Dictionary<int, MResLuaScript> scripts = new Dictionary<int, MResLuaScript>();
        internal MResLuaScript Get(int instanceId, uint definitionId)
        {
            if (!scripts.TryGetValue(instanceId, out var script))
            { script = new MResLuaScript(DeviceResourceCatalog.Current.Get(definitionId).Package); scripts.Add(instanceId, script); }
            return script;
        }
        internal void Clear() { foreach (var script in scripts.Values) script.Dispose(); scripts.Clear(); }
        internal void Remove(int instanceId)
        { if (scripts.TryGetValue(instanceId, out var script)) { script.Dispose(); scripts.Remove(instanceId); } }
        internal static void Contact(SimulationContext c, Entity ball, MarbleContact contact)
        {
            int id = contact.TargetId - 100000;
            if (!CampaignStateUtility.FindDevice(c, id, out var entity)) return;
            var device = c.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (!device.Placed || !(c.Physics is IMarblePhysicsCommands commands) || !commands.TryGetDevicePose(id, out var pose)) return;
            var api = new DeviceContactContext(c, ball, entity, contact, pose);
            try { c.DeviceScripts.Get(id, device.DefinitionId).Invoke("OnContact", api); }
            finally
            {
                api.Close();
                if (!c.Manager.Exists(entity)) c.DeviceScripts.Remove(id);
            }
        }
    }
}
