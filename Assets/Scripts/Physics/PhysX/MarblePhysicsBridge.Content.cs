using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge : IMarblePhysicsCommands
    {
        private readonly List<MarbleDevice> contentDevices = new List<MarbleDevice>();
        private RaycastHit[] pinHits = new RaycastHit[64];
        private readonly List<Collider> brokenPins = new List<Collider>();
        private void RefreshContentDevices()
        {
            contentDevices.Clear();
            foreach (var device in MarbleDevice.Active)
                if (device != null && device.InstanceId > 0 && device.gameObject.scene == scene) contentDevices.Add(device);
            contentDevices.Sort((a,b) => a.InstanceId.CompareTo(b.InstanceId));
        }
        public bool TryGetDevicePose(int instanceId, out DevicePose pose)
        {
            foreach (var device in MarbleDevice.Active)
                if (device != null && device.gameObject.scene == scene && device.InstanceId == instanceId)
                { pose = new DevicePose { Position = device.transform.position, Forward = device.Forward, Up = device.Up, Radius = device.Radius }; return true; }
            pose = default; return false;
        }
        public bool TryMove(MarbleKey key, Vector3 position, Vector3 velocity)
        {
            if (!bodies.TryGetValue(key, out var view) || view == null || !Finite(position) || !Finite(velocity)) return false;
            // Reject an obstructed exit; never teleport inside solid geometry.
            var candidates = new Collider[32];
            int count = physicsScene.OverlapSphere(position, view.Radius, candidates, ~0, QueryTriggerInteraction.Ignore);
            if (count == candidates.Length) return false;
            foreach (var candidate in candidates)
                if (candidate != null && candidate.attachedRigidbody != view.Body) return false;
            view.Body.position = position;
            view.Body.velocity = Vector3.ClampMagnitude(velocity, view.Settings.MaxSpeed > 0 ? view.Settings.MaxSpeed : 100);
            view.PreviousPosition = position;
            view.InsideDevices.Clear();
            view.Body.WakeUp(); return true;
        }
        public void SetProperties(MarbleKey key, MarblePhysicalSettings settings)
        {
            if (!bodies.TryGetValue(key, out var view) || view == null) return;
            view.HasSettings = true; view.Settings = settings;
            if (view.Radius != settings.Radius)
            { view.Radius = settings.Radius; view.transform.localScale = Vector3.one * (settings.Radius * 2); }
            if (view.Body.mass != settings.Mass) view.Body.mass = settings.Mass;
            if (view.Body.useGravity) view.Body.useGravity = false;
            if (view.RuntimeMaterial != null)
            {
                view.RuntimeMaterial.bounciness = Mathf.Clamp01(settings.Bounce);
                view.RuntimeMaterial.dynamicFriction = settings.Friction;
                view.RuntimeMaterial.staticFriction = settings.Friction;
            }
        }
        private void BeforeContentSubstep(float dt)
        {
            foreach (var view in bodies.Values)
            {
                if (view == null) continue;
                view.StepDevices.Clear();
                view.BeforeVelocity = view.Body.velocity;
                if (!view.HasSettings) continue;
                Vector3 acceleration = Physics.gravity * view.Settings.Gravity;
                Vector3 magneticForce = Vector3.zero;
                if (view.Settings.Magnetic && view.Settings.MagnetResponse > 0)
                    foreach (var device in contentDevices)
                    {
                        if (device.Kind != DeviceKind.Magnet) continue;
                        Vector3 delta = Vector3.ProjectOnPlane(device.transform.position - view.Body.position, device.Up);
                        float range = device.Range + view.Settings.MagneticRadius;
                        float distance = delta.magnitude;
                        if (distance < .001f || distance >= range) continue;
                        float weight = 1 - distance / range;
                        bool below = Vector3.Dot(view.Body.position - device.transform.position, device.Forward) >= 0;
                        magneticForce += delta / distance * (below ? 1 : -1) * device.Strength * view.Settings.MagnetResponse * weight * weight;
                    }
                view.Body.AddForce(acceleration * dt, ForceMode.VelocityChange);
                view.Body.AddForce(Vector3.ClampMagnitude(magneticForce, 100) * dt, ForceMode.Impulse);
                PreparePiercing(view, dt);
            }
        }
        private void AfterContentSubstep()
        {
            foreach (var view in bodies.Values)
            {
                if (view == null) continue;
                if (view.HasSettings) view.Body.velocity = Vector3.ClampMagnitude(view.Body.velocity, view.Settings.MaxSpeed);
                ReportTowerCrossings(view);
                view.InsideDevices.Clear();
                view.InsideDevices.UnionWith(view.StepDevices);
            }
        }
        private void PreparePiercing(MarbleBody view, float dt)
        {
            var sphere = view.GetComponent<SphereCollider>();
            for (int i = view.IgnoredPins.Count - 1; i >= 0; i--)
            {
                var pin = view.IgnoredPins[i];
                if (pin == null) { view.IgnoredPins.RemoveAt(i); continue; }
                if ((pin.ClosestPoint(view.Body.position) - view.Body.position).sqrMagnitude > (view.Radius + .02f) * (view.Radius + .02f))
                { Physics.IgnoreCollision(sphere, pin, false); view.IgnoredPins.RemoveAt(i); }
            }
            if (view.Settings.PierceCount <= 0 && view.Settings.PierceChance <= 0) return;
            Vector3 velocity = view.Body.velocity;
            int count;
            do
            {
                count = physicsScene.SphereCast(view.Body.position, view.Radius, velocity.normalized, pinHits,
                    velocity.magnitude * dt + .01f, ~0, QueryTriggerInteraction.Ignore);
                if (count < pinHits.Length) break;
                Array.Resize(ref pinHits, pinHits.Length * 2);
            } while (true);
            Array.Sort(pinHits, 0, count, Comparer<RaycastHit>.Create((a,b) => a.distance.CompareTo(b.distance)));
            var nearby = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                var collider = pinHits[i].collider;
                var pin = collider.GetComponentInParent<MarblePin>();
                if (pin == null || !pin.enabled || pin.ScoreMultiplier != 1 || pin.RushChanceAdd != 0 || collider.GetComponentInParent<MarbleDevice>() != null) continue;
                int id = collider.GetInstanceID(); nearby.Add(id);
                if (!view.PierceAttempts.Add(id) || view.IgnoredPins.Contains(collider)) continue;
                bool useCount = view.Settings.PierceCount > 0;
                if (!useCount && !MarbleRules.Roll(ref view.PhysicsRandom, view.Settings.PierceChance)) continue;
                Physics.IgnoreCollision(sphere, collider, true); view.IgnoredPins.Add(collider);
                if (useCount)
                {
                    view.Settings.PierceCount--;
                    Report(new MarbleContact { Key = view.Key, TargetId = pin.TargetId, Kind = MarbleContactKind.Pierced, Multiplier = 1, Position = view.Body.position });
                }
                Report(new MarbleContact { Key = view.Key, TargetId = pin.TargetId, Kind = MarbleContactKind.Pin, Multiplier = 1, Position = view.Body.position });
            }
            view.PierceAttempts.IntersectWith(nearby);
        }
        internal void ReportDevice(MarbleBody view, MarbleDevice device)
        {
            if (device == null || !device.isActiveAndEnabled || device.Kind == DeviceKind.Amplifier || device.Kind == DeviceKind.Magnet) return;
            view.StepDevices.Add(device.InstanceId);
            if (!view.InsideDevices.Add(device.InstanceId)) return;
            Report(device.Contact(view.Key, view.Body.position, view.Body.velocity.magnitude));
        }
        private void ReportTowerCrossings(MarbleBody view)
        {
            for (int i = 0; i < contentDevices.Count; i++)
            {
                var a = contentDevices[i];
                if (a.Kind != DeviceKind.Amplifier) continue;
                for (int j = i + 1; j < contentDevices.Count; j++)
                {
                    var b = contentDevices[j];
                    if (b.Kind != DeviceKind.Amplifier) continue;
                    Vector3 line = b.transform.position - a.transform.position;
                    float length = line.magnitude;
                    if (length > a.Range || length < .001f) continue;
                    Vector3 across = Vector3.Cross(a.Up, line / length);
                    float previous = Vector3.Dot(view.PreviousPosition - a.transform.position, across);
                    float current = Vector3.Dot(view.Body.position - a.transform.position, across);
                    if (previous * current >= 0) continue;
                    Vector3 crossing = Vector3.Lerp(view.PreviousPosition, view.Body.position, previous / (previous - current));
                    float along = Vector3.Dot(crossing - a.transform.position, line / length);
                    if (along < 0 || along > length || Math.Abs(Vector3.Dot(crossing - a.transform.position, a.Up)) > view.Radius + .5f) continue;
                    var contact = a.Contact(view.Key, crossing, view.Body.velocity.magnitude);
                    contact.LinkKey = ((long)a.InstanceId << 32) | (uint)b.InstanceId;
                    Report(contact);
                }
            }
        }
        public void FireBloodCannon(int instanceId)
        {
            if (!TryGetDevicePose(instanceId, out var pose)) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var pin in root.GetComponentsInChildren<MarblePin>())
                {
                    if (pin.ScoreMultiplier != 1 || pin.RushChanceAdd != 0 || pin.GetComponentInParent<MarbleDevice>() != null) continue;
                    Vector3 offset = Vector3.ProjectOnPlane(pin.transform.position - pose.Position, pose.Up);
                    float z = Vector3.Dot(offset, pose.Forward), x = Vector3.Dot(offset, Vector3.Cross(pose.Up, pose.Forward));
                    foreach (var collider in pin.GetComponentsInChildren<Collider>())
                    {
                        float tolerance = collider.bounds.extents.magnitude + .025f;
                        if (Math.Abs(Math.Abs(x) - Math.Abs(z)) > tolerance) continue;
                        collider.enabled = false; brokenPins.Add(collider);
                        foreach (var renderer in collider.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                    }
                }
        }
        public void RestorePins()
        {
            foreach (var collider in brokenPins)
                if (collider != null) { collider.enabled = true; foreach (var renderer in collider.GetComponentsInChildren<Renderer>()) renderer.enabled = true; }
            brokenPins.Clear();
        }
    }
}
