using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge : IDeviceTransientVisuals
    {
        private readonly Dictionary<int, GameObject> transientVisuals = new Dictionary<int, GameObject>();
        public bool TryCreateTransientVisual(int instanceId, int visualId, string nodePath, Vector3 position)
        {
            if (disposed || visualId <= 0 || transientVisuals.ContainsKey(visualId) || !Finite(position) || string.IsNullOrEmpty(nodePath)) return false;
            foreach (var device in MResDeviceBody.Active)
            {
                if (device == null || device.InstanceId != instanceId || device.gameObject.scene != scene) continue;
                var template = device.transform.Find(nodePath);
                if (template == null || template.GetComponentsInChildren<MeshFilter>(true).Length == 0) return false;
                var scale = device.transform.lossyScale;
                if (Mathf.Abs(scale.x * scale.y * scale.z) < 1e-12f) return false;
                GameObject visual = null;
                try
                {
                    visual = new GameObject("TransientVisual " + visualId); visual.SetActive(false);
                    visual.transform.SetParent(device.transform, false);
                    CopyMeshes(template, visual.transform);
                    var worldScale = template.lossyScale;
                    visual.transform.localScale = new Vector3(worldScale.x / scale.x, worldScale.y / scale.y, worldScale.z / scale.z);
                    visual.transform.SetPositionAndRotation(position, template.rotation);
                    transientVisuals.Add(visualId, visual); visual.SetActive(true); return true;
                }
                catch
                {
                    if (visual != null) DestroyTransient(visual);
                    transientVisuals.Remove(visualId); throw;
                }
            }
            return false;
        }
        private static void CopyMeshes(Transform source, Transform target)
        {
            // Copy presentation only. Never clone MRes behaviours, scripts, rigidbodies or colliders.
            var filter = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            if (filter != null && renderer != null)
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = target.gameObject.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                copy.enabled = renderer.enabled; copy.shadowCastingMode = renderer.shadowCastingMode; copy.receiveShadows = renderer.receiveShadows;
            }
            foreach (Transform child in source)
            {
                var node = new GameObject(child.name); node.transform.SetParent(target, false);
                node.transform.localPosition = child.localPosition; node.transform.localRotation = child.localRotation; node.transform.localScale = child.localScale;
                CopyMeshes(child, node.transform); node.SetActive(child.gameObject.activeSelf);
            }
        }
        public bool TryMoveTransientVisual(int visualId, Vector3 position)
        {
            if (disposed || !Finite(position) || !transientVisuals.TryGetValue(visualId, out var visual) || visual == null || !visual.activeInHierarchy) return false;
            visual.transform.position = position; return true;
        }
        public void RemoveTransientVisual(int visualId)
        {
            if (!transientVisuals.TryGetValue(visualId, out var visual)) return;
            transientVisuals.Remove(visualId); if (visual != null) DestroyTransient(visual);
        }
        private static void DestroyTransient(GameObject visual)
        {
            visual.SetActive(false);
            if (Application.isPlaying) Object.Destroy(visual); else Object.DestroyImmediate(visual);
        }
        private void ClearTransientVisuals()
        { foreach (int id in new List<int>(transientVisuals.Keys)) RemoveTransientVisual(id); }
    }
}
