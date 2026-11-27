using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal sealed class DeviceScriptSystem
    {
        private readonly Dictionary<int, DeviceLuaScript> scripts = new Dictionary<int, DeviceLuaScript>();
        private readonly Dictionary<int, (int round, string text)> statuses = new Dictionary<int, (int, string)>();
        private readonly List<DeviceLuaScript> deferredDisposals = new List<DeviceLuaScript>();
        private readonly SimulationContext context;
        private readonly IterationBuffer<KeyValuePair<int, Entity>> tickDevices = new IterationBuffer<KeyValuePair<int, Entity>>();
        private readonly IterationBuffer<KeyValuePair<int, Entity>> ballUpdateDevices = new IterationBuffer<KeyValuePair<int, Entity>>();
        private readonly IterationBuffer<KeyValuePair<int, Entity>> placedDeviceDispatch = new IterationBuffer<KeyValuePair<int, Entity>>();
        internal Func<DeviceResource, DeviceLuaScript> CreateScript { get; set; } = resource => resource.CreateScript();
        private int deviceDispatchDepth, callbackDepth, dispatchVersion;

        internal DeviceScriptSystem(SimulationContext context = null) => this.context = context;

        internal void InvalidateDispatch() => dispatchVersion++;
        internal void SetStatus(int instanceId, int round, string text)
        {
            text = text ?? string.Empty;
            statuses[instanceId] = (round, text.Length <= 256 ? text : text.Substring(0, 256));
        }
        internal string Status(int instanceId, int round) =>
            statuses.TryGetValue(instanceId, out var status) && status.round == round ? status.text : string.Empty;
        internal DeviceLuaScript Get(int instanceId, uint definitionId)
        {
            if (!scripts.TryGetValue(instanceId, out var script))
            {
                script = CreateScript(DeviceResourceCatalog.Current.Get(definitionId));
                Bind(instanceId, script);
            }
            return script;
        }

        internal void Bind(int instanceId, DeviceLuaScript script)
        {
            if (scripts.TryGetValue(instanceId, out var previous) && ReferenceEquals(previous, script)) return;
            scripts[instanceId] = script;
            if (context != null) script.CallbacksChanged += InvalidateDispatch;
            InvalidateDispatch();
            if (previous != null) ReleaseScript(previous);
            (context?.Physics as IMarbleDeviceScriptBinding)?.BindDeviceScript(instanceId, script);
        }

        internal void Clear()
        {
            foreach (var script in scripts.Values) ReleaseScript(script);
            scripts.Clear();
            statuses.Clear();
            tickDevices.Invalidate();
            ballUpdateDevices.Invalidate();
            InvalidateDispatch();
        }

        internal void AdoptPreparedScripts(DeviceScriptSystem prepared)
        {
            foreach (var pair in prepared.scripts)
            {
                scripts.Add(pair.Key, pair.Value);
                pair.Value.CallbacksChanged += InvalidateDispatch;
            }
            prepared.scripts.Clear();
            InvalidateDispatch();
        }

        internal void Remove(int instanceId)
        {
            statuses.Remove(instanceId);
            if (scripts.Remove(instanceId, out var script))
            {
                InvalidateDispatch();
                ReleaseScript(script);
                if (!scripts.ContainsKey(instanceId))
                    (context?.Physics as IMarbleDeviceScriptBinding)?.BindDeviceScript(instanceId, null);
            }
        }

        private void ReleaseScript(DeviceLuaScript script)
        {
            if (context != null) script.CallbacksChanged -= InvalidateDispatch;
            if (callbackDepth == 0) script.Dispose();
            else deferredDisposals.Add(script);
        }

        internal CallbackScope BeginCallback() => new CallbackScope(this);

        internal readonly struct CallbackScope : IDisposable
        {
            private readonly DeviceScriptSystem owner;
            internal CallbackScope(DeviceScriptSystem owner) { this.owner = owner; owner.callbackDepth++; }
            public void Dispose() => owner.EndCallback();
        }

        private void EndCallback()
        {
            try
            {
                if (callbackDepth != 1 || deferredDisposals.Count == 0) return;
                // The last gameplay context has closed. Keep any teardown-triggered removals in the same queue.
                for (int i = 0; i < deferredDisposals.Count; i++) deferredDisposals[i].Dispose();
                deferredDisposals.Clear();
            }
            finally { callbackDepth--; }
        }

        // Lifecycle callbacks also reach inventory and dormant instances; they do not imply a gameplay trigger.
        internal static bool InstanceEvent(SimulationContext c, int instanceId, string callback)
        {
            if (!AttributeRuntime.Enabled(c) || !c.TryGetDevice(instanceId, out var entity, out var device)) return false;
            return InstanceEvent(c, entity, device, callback);
        }

        private static bool InstanceEvent(SimulationContext c, Entity entity, OwnedDeviceData device, string callback)
        {
            int instanceId = device.InstanceId;
            var script = c.DeviceScripts.Get(instanceId, device.DefinitionId);
            if (!script.HasFunction(callback)) return false;
            var api = new DeviceEventContext(c, entity);
            using var scope = c.DeviceScripts.BeginCallback();
            try { script.Invoke(callback, api); return true; }
            catch (DeviceLuaException error) { Debug.LogException(error); return false; }
            finally
            {
                api.Close();
                script.RefreshScheduledCallbacks();
            }
        }
        internal static void RoundStart(SimulationContext c)
        {
            if (!AttributeRuntime.Enabled(c)) return;
            using var devices = c.OrderedDevices();
            foreach (var member in devices.Items)
                if (TryGetMember(c, member, out var device)) InstanceEvent(c, member.Value, device, "OnRoundStart");
        }

        private static bool TryGetMember(SimulationContext c, KeyValuePair<int, Entity> member, out OwnedDeviceData device) =>
            c.TryGetDevice(member.Key, out var current, out device) && current == member.Value;

        private void CollectCallbacks(List<KeyValuePair<int, Entity>> members, DeviceLuaCallbacks callback)
        {
            using var devices = context.OrderedDevices();
            foreach (var member in devices.Items)
                if (context.HasDeviceIdentity(member.Value, member.Key, out var device) &&
                    (Get(member.Key, device.DefinitionId).Callbacks & callback) != 0)
                    members.Add(member);
        }

        // Runs once at the START of a simulation step, including when no marbles exist.
        internal static void Tick(SimulationContext c)
        {
            if (!AttributeRuntime.Enabled(c) || !(c.Physics is IMarblePhysicsCommands commands)) return;
            var system = c.DeviceScripts;
            using var devices = system.tickDevices.Read(system.dispatchVersion, system,
                static (owner, members) => owner.CollectCallbacks(members, DeviceLuaCallbacks.Tick));
            // Placement, wear, and pending effects may change during the pass. Recheck them at each turn.
            foreach (var member in devices.Items)
                if (c.IsRunning && TryGetMember(c, member, out var device))
                    DeviceEvent(c, member.Value, device, commands, "OnTick");
        }

        internal static bool DeviceEvent(SimulationContext c, int instanceId, string callback, DeviceTransientSystem.Item ended = null, double? value = null)
        {
            if (!c.IsRunning || !AttributeRuntime.Enabled(c) ||
                !c.TryGetDevice(instanceId, out var entity, out var device) || !(c.Physics is IMarblePhysicsCommands commands)) return false;
            return DeviceEvent(c, entity, device, commands, callback, ended, value);
        }

        private static bool DeviceEvent(SimulationContext c, Entity entity, OwnedDeviceData device, IMarblePhysicsCommands commands,
            string callback, DeviceTransientSystem.Item ended = null, double? value = null)
        {
            int instanceId = device.InstanceId;
            if (!device.Placed || (!DeviceDurabilitySystem.CanRun(c, entity, device) && !CanComplete(c, entity, instanceId)) ||
                !commands.TryGetDevicePose(instanceId, out _)) return false;
            var script = c.DeviceScripts.Get(instanceId, device.DefinitionId);
            if (!script.HasFunction(callback)) return false;
            var api = new DeviceEventContext(c, entity, ended);
            using var scope = c.DeviceScripts.BeginCallback();
            try
            {
                if (value.HasValue) script.Invoke(callback, api, value.Value);
                else script.Invoke(callback, api);
                return true;
            }
            catch (DeviceLuaException error) { Debug.LogException(error); return false; }
            finally { api.Close(); }
        }
        internal static int ForEachPlacedDevice(SimulationContext c, int sourceId, uint definitionId, string callback, double value)
        {
            if (definitionId == 0 || string.IsNullOrEmpty(callback) || callback.Length > 64 || !MarbleRules.IsFinite(value))
                throw new System.ArgumentException("Invalid device event query.");
            if (!c.IsRunning || !AttributeRuntime.Enabled(c) || !DeviceDurabilitySystem.CanRun(c, sourceId)) return 0;
            if (c.DeviceScripts.deviceDispatchDepth >= 8) throw new System.InvalidOperationException("Device event nesting exceeds eight levels.");
            using var snapshot = c.DeviceScripts.placedDeviceDispatch.Rent();
            using (var devices = c.OrderedDevices())
                foreach (var member in devices.Items)
                    if (TryGetMember(c, member, out var device) && device.Placed && device.DefinitionId == definitionId)
                        snapshot.Items.Add(member);
            int invoked = 0; c.DeviceScripts.deviceDispatchDepth++;
            try
            {
                foreach (var member in snapshot.Items)
                    if (c.IsRunning && TryGetMember(c, member, out var device) && c.Physics is IMarblePhysicsCommands commands &&
                        DeviceEvent(c, member.Value, device, commands, callback, value: value)) invoked++;
            }
            finally { c.DeviceScripts.deviceDispatchDepth--; }
            return invoked;
        }
        internal static bool CanComplete(SimulationContext c, int instanceId) =>
            c.IsRunning && c.TryGetDevice(instanceId, out var entity, out var device) && device.Placed && CanComplete(c, entity, instanceId);

        private static bool CanComplete(SimulationContext c, Entity entity, int instanceId) =>
            DeviceDurabilitySystem.Continuing(c, entity) || HeldMotionSystem.Count(c, instanceId, true) > 0;
        internal static int ForEachBallInRadius(SimulationContext c, int instanceId, Vector3 center, float radius, string callback)
        {
            if (!SimulationContext.IsFinite(center) || !BallInteractionSystem.Finite(radius) || radius < 0 || radius > 100 ||
                string.IsNullOrEmpty(callback) || callback.Length > 64) throw new System.ArgumentException("Invalid ball query.");
            if (!c.IsRunning || !c.TryGetDevice(instanceId, out var entity, out var device) ||
                !DeviceDurabilitySystem.CanRun(c, entity, device) || !(c.Physics is IMarblePhysicsCommands commands) ||
                !commands.TryGetDevicePose(instanceId, out var pose)) return 0;
            if (!c.DeviceScripts.Get(instanceId, device.DefinitionId).HasFunction(callback)) return 0;
            // Freeze membership before invoking any Lua: new descendants cannot enter the current query.
            using var keys = c.BallDispatch.Rent();
            using (var orderedKeys = c.OrderedMarbleKeys())
            {
                foreach (var key in orderedKeys.Items)
                {
                    if (!c.TryGetFlying(key, out var ball) || c.Manager.HasComponent<HeldMotion>(ball)) continue;
                    long expiry = c.Manager.GetComponentData<Lifetime>(ball).ExpireTick;
                    if (expiry != 0 && expiry <= c.Round.Tick) continue;
                    var position = c.Manager.GetComponentData<Transform3D>(ball).Position;
                    float reach = radius + (float)AttributeRuntime.Get(c, ball, GameAttribute.BALL_RADIUS);
                    if ((position - center).sqrMagnitude <= reach * reach) keys.Items.Add(key);
                }
            }
            int visited = 0;
            foreach (var key in keys.Items)
            {
                if (!DeviceDurabilitySystem.CanRun(c, entity)) break;
                if (!c.TryGetFlying(key, out var ball) || c.Manager.HasComponent<HeldMotion>(ball)) continue;
                var contact = MarbleContact.ForDevice(key, instanceId, c.Manager.GetComponentData<Transform3D>(ball).Position);
                try { Invoke(c, ball, entity, device, contact, pose, callback); visited++; }
                catch (DeviceLuaException error) { Debug.LogException(error); }
            }
            return visited;
        }
        // Generic fixed-step callback. Lua chooses the affected region and composes motion commands.
        internal static void BallUpdate(SimulationContext c)
        {
            if (!AttributeRuntime.Enabled(c) || !(c.Physics is IMarblePhysicsCommands commands)) return;
            if (c.Round.Phase == (byte)RoundPhase.Draining &&
                c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds) return;
            var system = c.DeviceScripts;
            using var devices = system.ballUpdateDevices.Read(system.dispatchVersion, system,
                static (owner, members) => owner.CollectCallbacks(members, DeviceLuaCallbacks.BallUpdate));
            if (devices.Items.Count == 0 || c.Marbles.Count == 0) return;
            using var keys = c.OrderedMarbleKeys();
            foreach (var member in devices.Items)
            {
                var entity = member.Value;
                if (!TryGetMember(c, member, out var device) || !DeviceDurabilitySystem.CanRun(c, entity, device) ||
                    !commands.TryGetDevicePose(device.InstanceId, out var pose)) continue;
                foreach (var key in keys.Items)
                {
                    if (!DeviceDurabilitySystem.CanRun(c, entity)) break;
                    if (!c.TryGetFlying(key, out var ball) || c.Manager.HasComponent<HeldMotion>(ball) ||
                        c.Manager.GetComponentData<BallFeatures>(ball).MovedTick == c.Round.Tick) continue;
                    long expiry = c.Manager.GetComponentData<Lifetime>(ball).ExpireTick;
                    if (expiry != 0 && c.Round.Tick >= expiry) continue;
                    var script = system.Get(device.InstanceId, device.DefinitionId);
                    if ((script.Callbacks & DeviceLuaCallbacks.BallUpdate) == 0) break;
                    var contact = MarbleContact.ForDevice(key, device.InstanceId,
                        c.Manager.GetComponentData<Transform3D>(ball).Position,
                        c.Manager.GetComponentData<Motion3D>(ball).LinearVelocity.magnitude);
                    try { Invoke(c, ball, entity, device, contact, pose, "OnBallUpdate", script: script); }
                    catch (DeviceLuaException error) { Debug.LogException(error); }
                }
            }
        }
        internal static void Contact(SimulationContext c, Entity ball, MarbleContact contact)
        {
            int id = contact.DeviceInstanceId;
            if (!c.TryGetDevice(id, out var entity, out var device)) return;
            if (!DeviceDurabilitySystem.CanRun(c, entity, device) || !(c.Physics is IMarblePhysicsCommands commands) || !commands.TryGetDevicePose(id, out var pose)) return;
            if (contact.LinkKey == 0) TimedGravitySystem.Contact(c, ball, id);
            Invoke(c, ball, entity, device, contact, pose, "OnContact");
        }
        internal static void HoldEnd(SimulationContext c, Entity ball, HeldMotion hold)
        {
            if (!c.TryGetDevice(hold.SourceId, out var entity, out var device)) return;
            if (!device.Placed || !(c.Physics is IMarblePhysicsCommands commands) ||
                !commands.TryGetDevicePose(hold.SourceId, out var pose)) return;
            // Resolve the live pose and construct a fresh context, never retain the contact's Lua API.
            Invoke(c, ball, entity, device, hold.Contact, pose, "OnHoldEnd", hold);
        }
        private static void Invoke(SimulationContext c, Entity ball, Entity entity, OwnedDeviceData device,
            MarbleContact contact, DevicePose pose, string callback, HeldMotion? hold = null, DeviceLuaScript script = null)
        {
            var api = new DeviceContactContext(c, ball, entity, contact, pose, hold, callback != "OnBallUpdate");
            using var scope = c.DeviceScripts.BeginCallback();
            try { (script ?? c.DeviceScripts.Get(device.InstanceId, device.DefinitionId)).Invoke(callback, api); }
            finally { api.Close(); }
        }
    }
}
