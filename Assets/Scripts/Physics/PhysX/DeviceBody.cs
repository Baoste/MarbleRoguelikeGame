using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Physics identity and device configuration. Lua decides gameplay effects.</summary>
    public sealed class DeviceBody : MonoBehaviour
    {
        public int InstanceId { get; private set; }
        internal MarblePhysicsBridge Bridge { get; private set; }
        internal DeviceResource Resource { get; private set; }
        private float placementRadius;
        private Vector3 placementOffset;
        public Vector3 PlacementCenter => transform.position + (transform.parent
            ? transform.parent.TransformVector(transform.localRotation * placementOffset) : transform.rotation * placementOffset);
        public float Radius => placementRadius * (transform.parent ? Mathf.Max(Mathf.Abs(transform.parent.lossyScale.x),
            Mathf.Abs(transform.parent.lossyScale.y), Mathf.Abs(transform.parent.lossyScale.z)) : 1);
        public float Range { get; private set; }
        private DeviceDefinition definition;
        public float Strength => definition.Strength;
        public string LinkGroup { get; private set; }
        public DeviceLuaScript Script { get; private set; }
        public bool GameplayEnabled { get; private set; } = true;
        private float? configuredBounce;
        internal float Bounce => configuredBounce.GetValueOrDefault();
        private List<PhysicMaterial> runtimeMaterials;
        private Collider[] colliders;
        private readonly List<Collider> disabledByUsage = new List<Collider>();
        internal void SetGameplayEnabled(bool enabled)
        {
            if (GameplayEnabled == enabled) return;
            GameplayEnabled = enabled;
            if (!enabled)
            {
                foreach (var collider in colliders)
                    if (collider.enabled) { disabledByUsage.Add(collider); collider.enabled = false; }
            }
            else
            {
                foreach (var collider in disabledByUsage) if (collider != null) collider.enabled = true;
                disabledByUsage.Clear();
            }
        }
        public Vector3 Forward => transform.rotation * Vector3.back;
        public Vector3 Up => transform.up;
        private Quaternion placementRotation;
        internal void SetPlacement(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            placementRotation = transform.localRotation;
        }

        internal void SetYaw(float degrees)
        {
            var rotation = placementRotation * Quaternion.AngleAxis(degrees % 360, Vector3.up);
            if (Quaternion.Angle(transform.localRotation, rotation) < .001f) return;
            transform.localRotation = rotation;
            Physics.SyncTransforms();
        }
        private readonly Dictionary<string, BoxCollider> fieldBoxes = new Dictionary<string, BoxCollider>();
        internal BoxCollider FindFieldBox(string nodePath)
        {
            if (nodePath == null) return null;
            if (fieldBoxes.TryGetValue(nodePath, out var box) && box != null) return box;
            var node = nodePath.Length == 0 ? transform : transform.Find(nodePath);
            box = node != null ? node.GetComponent<BoxCollider>() : null;
            if (box != null) fieldBoxes[nodePath] = box;
            return box;
        }
        internal void Initialize(MarblePhysicsBridge bridge, int instanceId, DeviceDefinition definition,
            DeviceResource resource, DeviceLuaScript script)
        {
            Bridge = bridge;
            Resource = resource;
            this.definition = definition;
            InstanceId = instanceId; placementRadius = definition.PlacementRadius; Range = definition.Range;
            placementOffset = new Vector3(definition.PlacementOffsetX, definition.PlacementOffsetY, definition.PlacementOffsetZ);
            LinkGroup = definition.LinkGroup; Script = script;
            configuredBounce = definition.Bounce;
            colliders = GetComponentsInChildren<Collider>(true);
            if (configuredBounce.HasValue) ApplyBounce();
            if (script == null) return;
            var behaviour = gameObject.AddComponent<DeviceLuaBehaviour>();
            behaviour.Initialize(script, resource.Prefab.transform);
            DeviceLuaPhysicsRelay.Attach(GetComponentsInChildren<Transform>(true), behaviour);
        }
        private void ApplyBounce()
        {
            if (runtimeMaterials == null)
            {
                runtimeMaterials = new List<PhysicMaterial>();
                foreach (var collider in colliders)
                {
                    if (collider.isTrigger) continue;
                    // Retain the authored friction settings and keep the shared asset untouched.
                    var source = collider.sharedMaterial;
                    var material = source != null ? Instantiate(source) : new PhysicMaterial();
                    runtimeMaterials.Add(material);
                    collider.sharedMaterial = material;
                }
            }
            foreach (var material in runtimeMaterials) material.bounciness = Bounce;
        }
        private void OnDestroy() => Release();
        internal void Release()
        {
            GetComponent<DeviceLuaBehaviour>()?.Release();
            Bridge = null;
            if (runtimeMaterials == null) return;
            foreach (var material in runtimeMaterials)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            runtimeMaterials = null;
        }
        public MarbleContact Contact(MarbleKey key, Vector3 position, float speed = 0) => new MarbleContact
        { Key = key, TargetId = checked(100000 + InstanceId), Kind = MarbleContactKind.Device, Multiplier = 1, Position = position, Speed = speed, RushDisabled = true };
    }
}
