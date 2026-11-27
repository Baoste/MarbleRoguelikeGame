using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MarblesECS.Presentation
{
    public static class DeviceView
    {
        public static DeviceViewHandle CreatePreview(OwnedDeviceSnapshot device, Transform parent)
        {
            var prefab = DeviceResourceCatalog.Current.Get(device.DefinitionId).Prefab;
            var view = CreateModel(prefab.transform, parent, device.Name + " Preview");
            view.Root.transform.localScale = prefab.transform.localScale;
            return view;
        }

        internal static DeviceViewHandle CreateModel(Transform template, Transform parent, string name)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            var root = new GameObject(name) { layer = template.gameObject.layer };
            root.SetActive(false);
            var view = new DeviceViewHandle(root);
            try
            {
                root.transform.SetParent(parent, false);
                CopyMeshes(template, root.transform);
                return view;
            }
            catch
            {
                view.Dispose();
                throw;
            }
        }

        private static void CopyMeshes(Transform source, Transform target)
        {
            if (source.TryGetComponent<MeshFilter>(out var filter) && source.TryGetComponent<MeshRenderer>(out var renderer))
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = target.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.enabled = renderer.enabled;
                copy.shadowCastingMode = renderer.shadowCastingMode;
                copy.receiveShadows = renderer.receiveShadows;
                copy.lightProbeUsage = renderer.lightProbeUsage;
                copy.reflectionProbeUsage = renderer.reflectionProbeUsage;
                copy.motionVectorGenerationMode = renderer.motionVectorGenerationMode;
                copy.allowOcclusionWhenDynamic = renderer.allowOcclusionWhenDynamic;
                copy.renderingLayerMask = renderer.renderingLayerMask;
                copy.sortingLayerID = renderer.sortingLayerID;
                copy.sortingOrder = renderer.sortingOrder;
            }
            foreach (Transform child in source)
            {
                if (child.name == DeviceResourceCatalog.PlacementVolumeName) continue;
                var node = new GameObject(child.name) { layer = child.gameObject.layer };
                node.transform.SetParent(target, false);
                node.transform.SetLocalPositionAndRotation(child.localPosition, child.localRotation);
                node.transform.localScale = child.localScale;
                CopyMeshes(child, node.transform);
                node.SetActive(child.gameObject.activeSelf);
            }
        }
    }

    public sealed class DeviceViewHandle : IDisposable
    {
        public GameObject Root { get; private set; }

        internal DeviceViewHandle(GameObject root) => Root = root;

        public void Dispose()
        {
            var root = Root;
            Root = null;
            if (root == null) return;
            root.SetActive(false);
            if (Application.isPlaying) Object.Destroy(root);
            else Object.DestroyImmediate(root);
        }
    }
}
