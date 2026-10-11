using System.Collections.Generic;
using MRes;
using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Physics identity and package configuration; gameplay decisions belong to Lua.</summary>
    public sealed class MResDeviceBody : MonoBehaviour
    {
        internal static readonly HashSet<MResDeviceBody> Active = new HashSet<MResDeviceBody>();
        public int InstanceId { get; private set; }
        public float Radius { get; private set; }
        public float Range { get; private set; }
        public float Strength { get; private set; }
        public string LinkGroup { get; private set; }
        public MResLuaScript Script { get; private set; }
        public bool GameplayEnabled { get; private set; } = true;
        private readonly List<Collider> disabledByUsage = new List<Collider>();
        internal void SetGameplayEnabled(bool enabled)
        {
            if (GameplayEnabled == enabled) return;
            GameplayEnabled = enabled;
            if (!enabled)
            {
                foreach (var collider in GetComponentsInChildren<Collider>(true))
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
        public void Initialize(int instanceId, DeviceDefinition definition, MResLuaScript script)
        {
            InstanceId = instanceId; Radius = definition.Radius; Range = definition.Range;
            Strength = definition.Strength; LinkGroup = definition.LinkGroup; Script = script;
        }
        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);
        public MarbleContact Contact(MarbleKey key, Vector3 position, float speed = 0) => new MarbleContact
        { Key = key, TargetId = checked(100000 + InstanceId), Kind = MarbleContactKind.Device, Multiplier = 1, Position = position, Speed = speed, RushDisabled = true };
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveDevices() => Active.Clear();
    }
}
