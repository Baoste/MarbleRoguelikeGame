using System.Collections.Generic;
using MRes;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal sealed class DeviceScriptSystem
    {
        private readonly Dictionary<int, MResLuaScript> scripts = new Dictionary<int, MResLuaScript>();
        private readonly Dictionary<int, (int round, string text)> statuses = new Dictionary<int, (int, string)>();
        internal void SetStatus(int instanceId, int round, string text)
        {
            text = text ?? string.Empty;
            statuses[instanceId] = (round, text.Length <= 256 ? text : text.Substring(0, 256));
        }
        internal string Status(int instanceId, int round) =>
            statuses.TryGetValue(instanceId, out var status) && status.round == round ? status.text : string.Empty;
        internal MResLuaScript Get(int instanceId, uint definitionId)
        {
            if (!scripts.TryGetValue(instanceId, out var script))
            { script = new MResLuaScript(DeviceResourceCatalog.Current.Get(definitionId).Package); scripts.Add(instanceId, script); }
            return script;
        }
        internal void Clear() { foreach (var script in scripts.Values) script.Dispose(); scripts.Clear(); statuses.Clear(); }
        internal void Remove(int instanceId)
        { if (scripts.TryGetValue(instanceId, out var script)) { script.Dispose(); scripts.Remove(instanceId); } statuses.Remove(instanceId); }
        // Runs once at the START of a simulation step, including when no marbles exist.
        internal static void Tick(SimulationContext c)
        {
            if (!AttributeRuntime.Enabled(c)) return;
            var ids = new List<int>();
            using (var entities = c.DeviceQuery.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities) ids.Add(c.Manager.GetComponentData<OwnedDeviceData>(entity).InstanceId);
            ids.Sort();
            foreach (int id in ids) DeviceEvent(c, id, "OnTick");
        }
        internal static bool DeviceEvent(SimulationContext c, int instanceId, string callback)
        {
            if (!c.IsRunning || !AttributeRuntime.Enabled(c) ||
                !CampaignStateUtility.FindDevice(c, instanceId, out var entity)) return false;
            var device = c.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (!DeviceDurabilitySystem.Available(c, device) || !(c.Physics is IMarblePhysicsCommands commands) ||
                !commands.TryGetDevicePose(instanceId, out _)) return false;
            var script = c.DeviceScripts.Get(instanceId, device.DefinitionId);
            if (!script.HasFunction(callback)) return false;
            var api = new DeviceEventContext(c, entity);
            try { script.Invoke(callback, api); return true; }
            catch (MResLuaException error) { Debug.LogException(error); return false; }
            finally { api.Close(); }
        }
        // Generic fixed-step callback. Lua chooses the affected region and composes motion commands.
        internal static void BallUpdate(SimulationContext c)
        {
            if (!AttributeRuntime.Enabled(c) || !(c.Physics is IMarblePhysicsCommands commands)) return;
            if (c.Round.Phase == (byte)RoundPhase.Draining &&
                c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds) return;
            var devices = new List<Entity>();
            using (var entities = c.DeviceQuery.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities) devices.Add(entity);
            devices.Sort((a, b) => c.Manager.GetComponentData<OwnedDeviceData>(a).InstanceId.CompareTo(
                c.Manager.GetComponentData<OwnedDeviceData>(b).InstanceId));
            var keys = new List<MarbleKey>(c.Marbles.Keys);
            keys.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            foreach (var entity in devices)
            {
                if (!c.Manager.Exists(entity)) continue;
                var device = c.Manager.GetComponentData<OwnedDeviceData>(entity);
                if (!DeviceDurabilitySystem.Available(c, device) || !commands.TryGetDevicePose(device.InstanceId, out var pose)) continue;
                var script = c.DeviceScripts.Get(device.InstanceId, device.DefinitionId);
                if (!script.HasFunction("OnBallUpdate")) continue;
                foreach (var key in keys)
                {
                    if (!c.Manager.Exists(entity)) break;
                    if (!DeviceDurabilitySystem.Available(c, device.InstanceId)) break;
                    if (!c.TryGetFlying(key, out var ball) || c.Manager.HasComponent<HeldMotion>(ball) ||
                        c.Manager.GetComponentData<BallFeatures>(ball).MovedTick == c.Round.Tick) continue;
                    long expiry = c.Manager.GetComponentData<Lifetime>(ball).ExpireTick;
                    if (expiry != 0 && c.Round.Tick >= expiry) continue;
                    var contact = new MarbleContact { Key = key, Kind = MarbleContactKind.Device,
                        TargetId = checked(100000 + device.InstanceId), Multiplier = 1, RushDisabled = true,
                        Position = c.Manager.GetComponentData<Transform3D>(ball).Position,
                        Speed = c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity.magnitude };
                    try { Invoke(c, ball, entity, device, contact, pose, "OnBallUpdate"); }
                    catch (MResLuaException error) { Debug.LogException(error); }
                }
            }
        }
        internal static void Contact(SimulationContext c, Entity ball, MarbleContact contact)
        {
            int id = contact.TargetId - 100000;
            if (!CampaignStateUtility.FindDevice(c, id, out var entity)) return;
            var device = c.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (!DeviceDurabilitySystem.Available(c, device) || !(c.Physics is IMarblePhysicsCommands commands) || !commands.TryGetDevicePose(id, out var pose)) return;
            Invoke(c, ball, entity, device, contact, pose, "OnContact");
        }
        internal static void HoldEnd(SimulationContext c, Entity ball, HeldMotion hold)
        {
            if (!CampaignStateUtility.FindDevice(c, hold.SourceId, out var entity)) return;
            var device = c.Manager.GetComponentData<OwnedDeviceData>(entity);
            if (!device.Placed || !(c.Physics is IMarblePhysicsCommands commands) ||
                !commands.TryGetDevicePose(hold.SourceId, out var pose)) return;
            // Resolve the live pose and construct a fresh context, never retain the contact's Lua API.
            Invoke(c, ball, entity, device, hold.Contact, pose, "OnHoldEnd", hold);
        }
        private static void Invoke(SimulationContext c, Entity ball, Entity entity, OwnedDeviceData device,
            MarbleContact contact, DevicePose pose, string callback, HeldMotion? hold = null)
        {
            var api = new DeviceContactContext(c, ball, entity, contact, pose, hold);
            try { c.DeviceScripts.Get(device.InstanceId, device.DefinitionId).Invoke(callback, api); }
            finally
            {
                api.Close();
                if (!c.Manager.Exists(entity)) c.DeviceScripts.Remove(device.InstanceId);
            }
        }
    }
}
