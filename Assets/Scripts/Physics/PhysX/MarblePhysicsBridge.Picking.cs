using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge
    {
        private RaycastHit[] devicePickHits = new RaycastHit[32];
        /// <summary>Pick the nearest clickable device in this simulation's local physics scene, including trigger shapes.</summary>
        public bool TryPickDevice(Ray ray, out int instanceId)
        {
            instanceId = 0;
            if (disposed || !Finite(ray.origin) || !Finite(ray.direction) || ray.direction.sqrMagnitude < 1e-12f) return false;
            int count;
            do
            {
                count = physicsScene.Raycast(ray.origin, ray.direction, devicePickHits, 1000, ~0, QueryTriggerInteraction.Collide);
                if (count < devicePickHits.Length) break;
                Array.Resize(ref devicePickHits, checked(devicePickHits.Length * 2));
            } while (true);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = devicePickHits[i];
                var device = hit.collider.GetComponentInParent<MResDeviceBody>();
                if (device == null || !device.isActiveAndEnabled || !device.GameplayEnabled || device.Script == null ||
                    !device.Script.HasFunction("OnActivate") || hit.distance >= nearest) continue;
                nearest = hit.distance; instanceId = device.InstanceId;
            }
            return instanceId != 0;
        }
    }
}
