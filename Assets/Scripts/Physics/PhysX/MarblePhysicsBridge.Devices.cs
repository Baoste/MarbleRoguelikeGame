using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge : IMarbleDevicePhysics, IMarbleDeviceScriptBinding
    {
        private readonly Dictionary<int, DeviceBody> devices = new Dictionary<int, DeviceBody>();
        private readonly List<DeviceBody> contentDevices = new List<DeviceBody>();
        private int fieldMembershipVersion;
        private DeviceScriptSystem deviceCallbacks;
        private readonly DevicePlacementObstacles placementObstacles = new DevicePlacementObstacles();
        private Transform deviceOrigin, devicePlane;
        private BoardPlacementSurface deviceSurface;
        private Transform[] deviceObstacles;
        private float devicePlaneDistance;

        internal IReadOnlyList<DeviceBody> PlacedDeviceBodies => contentDevices;
        internal int DeviceRevision { get; private set; }

        /// <summary>Configures the X/Z coordinate frame, optional surface and physical placement obstacles.</summary>
        public void ConfigureDevicePlacement(Transform origin, Transform plane = null, float planeDistance = 0,
            BoardPlacementSurface surface = null, Transform[] obstacles = null)
        {
            if (disposed || devices.Count != 0) throw new InvalidOperationException("Configure placement before creating devices.");
            if (origin == null || origin.gameObject.scene != scene || (plane != null && plane.gameObject.scene != scene) ||
                !MarbleRules.IsFinite(planeDistance)) throw new ArgumentException("Device placement requires a valid local frame.");
            deviceOrigin = origin;
            devicePlane = plane != null ? plane : origin;
            devicePlaneDistance = planeDistance;
            deviceSurface = surface;
            deviceObstacles = obstacles;
        }

        internal bool TryGetPlacementPose(float x, float z, float angle, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (disposed || !scene.isLoaded || !MarbleRules.IsFinite(x) || !MarbleRules.IsFinite(z) ||
                !MarbleRules.IsFinite(angle)) return false;
            var point = new Vector3(x, 0, z);
            if (ReferenceEquals(deviceOrigin, null)) position = point;
            else
            {
                if (deviceOrigin == null || !deviceOrigin.gameObject.activeInHierarchy || deviceOrigin.gameObject.scene != scene ||
                    devicePlane == null || devicePlane.gameObject.scene != scene) return false;
                var scale = deviceOrigin.lossyScale;
                if (!Finite(scale) || scale.x == 0 || scale.y == 0 || scale.z == 0) return false;
                rotation = devicePlane.rotation;
                var plane = new Plane(rotation * Vector3.up, devicePlane.position);
                var boardPoint = deviceOrigin.TransformPoint(point);
                var normal = deviceOrigin.up;
                float denominator = Vector3.Dot(plane.normal, normal);
                position = Mathf.Abs(denominator) > 0.0001f
                    ? boardPoint - normal * (plane.GetDistanceToPoint(boardPoint) / denominator)
                    : plane.ClosestPointOnPlane(boardPoint);
                position += plane.normal * devicePlaneDistance;
            }
            rotation *= Quaternion.Euler(0, angle, 0);
            return Finite(position) && SimulationContext.IsFinite(rotation);
        }

        public bool CanPlaceDevice(DevicePlacement placement, float clearance, bool avoidMarbles)
        {
            if (!TryGetPlacementPose(placement.X, placement.Z, placement.Angle, out var position, out var rotation)) return false;
            var definition = placement.Definition;
            var offset = new Vector3(definition.PlacementOffsetX, definition.PlacementOffsetY, definition.PlacementOffsetZ);
            var center = position + (deviceOrigin != null
                ? deviceOrigin.TransformVector(Quaternion.Inverse(deviceOrigin.rotation) * rotation * offset)
                : rotation * offset);
            var scale = deviceOrigin != null ? deviceOrigin.lossyScale : Vector3.one;
            float radius = (definition.PlacementRadius + clearance) *
                Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            return Finite(center) && MarbleRules.IsFinite(radius) && radius > 0 &&
                (deviceSurface == null || deviceSurface.ContainsWorldPoint(center, radius)) &&
                (!avoidMarbles || IsClearOfMarbles(center, radius)) &&
                placementObstacles.IsClear(physicsScene, deviceObstacles, center, radius, GeneratedBoardLayout);
        }

        internal bool TryGetDeviceBody(int id, out DeviceBody body) =>
            devices.TryGetValue(id, out body) && body != null && body.isActiveAndEnabled;

        public bool TrySetDevicePlacement(DevicePlacement placement, Func<int, uint, DeviceLuaScript> getScript)
        {
            if (placement.InstanceId <= 0 || placement.Definition == null ||
                !TryGetPlacementPose(placement.X, placement.Z, placement.Angle, out var position, out var rotation)) return false;
            if (devices.TryGetValue(placement.InstanceId, out var existing))
            {
                if (existing == null || !existing.isActiveAndEnabled || existing.Resource.Definition.Id != placement.Definition.Id)
                    return false;
                existing.SetPlacement(position, rotation);
                Physics.SyncTransforms();
                return true;
            }
            var candidate = CreateDevice(placement, getScript, position, rotation);
            if (candidate == null) return false;
            RegisterDevice(candidate);
            Physics.SyncTransforms();
            return true;
        }

        public bool TryReplaceDevicePlacements(IReadOnlyList<DevicePlacement> placements, Func<int, uint, DeviceLuaScript> getScript)
        {
            if (disposed) return false;
            var staged = new List<DeviceBody>(placements.Count);
            var ids = new HashSet<int>();
            try
            {
                foreach (var placement in placements)
                {
                    if (placement.InstanceId <= 0 || placement.Definition == null || !ids.Add(placement.InstanceId) ||
                        !TryGetPlacementPose(placement.X, placement.Z, placement.Angle, out var position, out var rotation))
                        return false;
                    var candidate = CreateDevice(placement, getScript, position, rotation);
                    if (candidate == null) return false;
                    staged.Add(candidate);
                }
                ClearDevices();
                foreach (var body in staged) RegisterDevice(body);
                staged.Clear();
                Physics.SyncTransforms();
                return true;
            }
            finally
            {
                foreach (var body in staged) DestroyDeviceObject(body.gameObject);
            }
        }

        private DeviceBody CreateDevice(DevicePlacement placement, Func<int, uint, DeviceLuaScript> getScript,
            Vector3 position, Quaternion rotation)
        {
            GameObject root = null;
            try
            {
                var resource = DeviceResourceCatalog.Current.Get(placement.Definition.Id);
                root = new GameObject(placement.Definition.Name + " #" + placement.InstanceId);
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetParent(deviceOrigin, false);
                DevicePhysicsGeometry.Copy(resource.Prefab, root);
                var body = root.AddComponent<DeviceBody>();
                body.SetPlacement(position, rotation);
                body.Initialize(this, placement.InstanceId, placement.Definition, resource,
                    getScript(placement.InstanceId, placement.Definition.Id));
                return body;
            }
            catch (Exception error)
            {
                if (root != null) DestroyDeviceObject(root);
                Debug.LogException(error);
                return null;
            }
        }

        private void RegisterDevice(DeviceBody body)
        {
            devices.Add(body.InstanceId, body);
            contentDevices.Add(body);
            contentDevices.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
            fieldMembershipVersion++;
            body.gameObject.SetActive(true);
            DeviceRevision++;
        }

        public void RemoveDevice(int id)
        {
            if (!devices.Remove(id, out var body)) return;
            contentDevices.Remove(body);
            fieldMembershipVersion++;
            if (body != null) DestroyDeviceObject(body.gameObject);
            DeviceRevision++;
        }

        public void ClearDevices()
        {
            if (devices.Count == 0) return;
            foreach (var body in contentDevices)
                if (body != null) DestroyDeviceObject(body.gameObject);
            contentDevices.Clear();
            devices.Clear();
            fieldDevices.Invalidate();
            DeviceRevision++;
        }

        internal void NotifyDeviceDestroyed(DeviceBody body)
        {
            if (!devices.TryGetValue(body.InstanceId, out var registered) || !ReferenceEquals(registered, body)) return;
            devices.Remove(body.InstanceId);
            contentDevices.Remove(body);
            fieldMembershipVersion++;
            DeviceRevision++;
        }

        void IMarbleDeviceScriptBinding.BindDeviceCallbacks(DeviceScriptSystem callbacks) => deviceCallbacks = callbacks;

        internal void InvalidateFieldDevices() => fieldMembershipVersion++;

        void IMarbleDeviceScriptBinding.BindDeviceScript(int instanceId, DeviceLuaScript script)
        {
            if (!devices.TryGetValue(instanceId, out var body) || body == null || ReferenceEquals(body.Script, script)) return;
            body.BindScript(script);
            fieldMembershipVersion++;
        }

        private static void DestroyDeviceObject(GameObject root)
        {
            root.GetComponent<DeviceBody>()?.Release();
            root.SetActive(false);
            if (Application.isPlaying) Object.Destroy(root);
            else Object.DestroyImmediate(root);
        }
    }
}
