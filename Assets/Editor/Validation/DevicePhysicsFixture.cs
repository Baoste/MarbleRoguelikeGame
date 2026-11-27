using System;
using UnityEngine;

namespace MarblesECS.Editor
{
    // Low-level fixture for scenarios that position or overlap devices outside normal placement rules.
    internal sealed class DevicePhysicsFixture : IDisposable
    {
        private readonly PhysX.MarblePhysicsBridge bridge;
        private readonly PhysX.DeviceBody body;
        internal GameObject Root => body == null ? null : body.gameObject;
        internal Transform Template => body.Resource.Prefab.transform;

        private DevicePhysicsFixture(PhysX.MarblePhysicsBridge bridge, PhysX.DeviceBody body)
        {
            this.bridge = bridge;
            this.body = body;
        }

        internal static DevicePhysicsFixture Place(MarbleSimulation simulation, OwnedDeviceSnapshot device, Transform board, DeviceLuaScript script)
        {
            var context = simulation.Context;
            var bridge = (PhysX.MarblePhysicsBridge)context.Physics;
            if (bridge.PlacedDeviceBodies.Count == 0) bridge.ConfigureDevicePlacement(board);
            var definition = CampaignStateUtility.Device(context, device.DefinitionId);
            var placement = new DevicePlacement(device.InstanceId, definition, device.X, device.Z, device.Angle);
            if (!bridge.TrySetDevicePlacement(placement, (_, _) => script) || !bridge.TryGetDeviceBody(device.InstanceId, out var body))
                throw new InvalidOperationException("Could not create the fixture's device body.");
            DeviceDurabilitySystem.Refresh(context);
            return new DevicePhysicsFixture(bridge, body);
        }

        public void Dispose()
        {
            if (body != null && bridge.TryGetDeviceBody(body.InstanceId, out var current) && current == body)
                bridge.RemoveDevice(body.InstanceId);
        }
    }
}
