using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    [Preserve]
    public sealed class DeviceLuaObject
    {
        private readonly GameObject gameObject;
        private readonly DeviceMetadata metadata;
        private readonly Transform authoredVisual;
        private Transform model;
        private List<(string path, bool visible)> visibility;

        public DeviceLuaObject(GameObject gameObject, DeviceMetadata metadata) : this(gameObject, metadata, null) { }
        internal DeviceLuaObject(GameObject gameObject, DeviceMetadata metadata, Transform authoredVisual)
        {
            this.gameObject = gameObject;
            this.metadata = metadata;
            this.authoredVisual = authoredVisual;
        }
        public Vector3 GetPosition() => gameObject.transform.position;
        public void SetPosition(float x, float y, float z) => gameObject.transform.position = new Vector3(x, y, z);
        public void Translate(float x, float y, float z) => gameObject.transform.Translate(x, y, z, Space.World);
        public void Rotate(float x, float y, float z) => gameObject.transform.Rotate(x, y, z, Space.Self);
        public void AddForce(float x, float y, float z)
        {
            var body = gameObject.GetComponentInParent<Rigidbody>();
            if (body != null) body.AddForce(x, y, z, ForceMode.Force);
        }
        public void SetVelocity(float x, float y, float z)
        {
            var body = gameObject.GetComponentInParent<Rigidbody>();
            if (body == null) return;
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = new Vector3(x, y, z);
#else
            body.velocity = new Vector3(x, y, z);
#endif
        }
        public string GetName() => metadata?.name;
        public string GetDescription() => metadata?.description;
        public string GetRarity() => metadata?.rarity ?? "common";
        public int GetDurability() => metadata?.durability ?? 20;
        public string GetItemType() => metadata?.type.ToString();
        /// <summary>Changes only renderer visibility within this instance; never activates nodes or changes physics.</summary>
        public bool TrySetRenderersVisible(string nodePath, bool visible)
        {
            if (gameObject == null || nodePath == null || nodePath.Length > 256) return false;
            if (nodePath.Length != 0)
                foreach (string part in nodePath.Split('/'))
                    if (part.Length == 0 || part == "." || part == ".." || part.Contains("\\")) return false;
            if (authoredVisual == null) return SetVisibility(gameObject.transform, nodePath, visible);
            var authored = nodePath.Length == 0 ? authoredVisual : authoredVisual.Find(nodePath);
            if (authored == null || authored.GetComponentInChildren<Renderer>(true) == null) return false;
            visibility ??= new List<(string path, bool visible)>();
            for (int i = 0; i < visibility.Count; i++)
                if (visibility[i].path == nodePath) { visibility.RemoveAt(i); break; }
            visibility.Add((nodePath, visible));
            if (model != null) SetVisibility(model, nodePath, visible);
            return true;
        }
        internal void AttachPresentation(Transform value)
        {
            model = value;
            if (model == null || visibility == null) return;
            foreach (var change in visibility) SetVisibility(model, change.path, change.visible);
        }
        private static bool SetVisibility(Transform root, string nodePath, bool visible)
        {
            var node = nodePath.Length == 0 ? root : root.Find(nodePath);
            if (node == null) return false;
            var renderers = node.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers) renderer.enabled = visible;
            return renderers.Length != 0;
        }
        internal static DeviceLuaObject Other(GameObject go)
        {
            var owner = go.GetComponentInParent<DeviceLuaBehaviour>();
            return new DeviceLuaObject(go, owner?.Metadata);
        }
    }
}
