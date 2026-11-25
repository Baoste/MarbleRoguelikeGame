using System;
using MarblesECS.PhysX;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MarblesECS.Presentation
{
    public static class DeviceView
    {
        /// <summary>Returns an inactive prefab instance. The caller positions it before activation.</summary>
        public static DeviceViewHandle Create(OwnedDeviceSnapshot device, Transform parent, DeviceLuaScript script, bool preview = false)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (!preview && script == null) throw new ArgumentNullException(nameof(script));

            var resource = DeviceResourceCatalog.Current.Get(device.DefinitionId);
            var root = InstantiateInactive(resource.Prefab, parent);
            var view = new DeviceViewHandle(root);
            try
            {
                root.name = device.Name + (preview ? " Preview" : " #" + device.InstanceId);
                root.transform.Find(DeviceResourceCatalog.PlacementVolumeName).GetComponent<SphereCollider>().enabled = false;
                if (preview)
                {
                    foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                    {
                        body.isKinematic = true;
                        body.detectCollisions = false;
                    }
                }
                else
                {
                    var behaviour = root.AddComponent<DeviceLuaBehaviour>();
                    behaviour.Initialize(script);
                    DeviceLuaPhysicsRelay.Attach(root.GetComponentsInChildren<Transform>(true), behaviour);
                    root.AddComponent<DeviceBody>().Initialize(device.InstanceId, resource.Definition, script);
                    if (!string.IsNullOrEmpty(resource.Definition.LinkGroup)) root.AddComponent<DeviceLinkView>();
                }
                return view;
            }
            catch
            {
                view.Dispose();
                throw;
            }
        }

        private static GameObject InstantiateInactive(GameObject prefab, Transform parent)
        {
            if (!prefab.activeSelf) return Object.Instantiate(prefab, parent, false);

            // Prevent Awake and OnEnable from running before the view has its board pose and script.
            var staging = new GameObject("Device setup");
            staging.SetActive(false);
            staging.transform.SetParent(parent, false);
            try
            {
                var root = Object.Instantiate(prefab, staging.transform, false);
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                return root;
            }
            finally { DeviceViewHandle.Destroy(staging); }
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
            root.GetComponent<DeviceLuaBehaviour>()?.Release();
            root.SetActive(false);
            Destroy(root);
        }

        internal static void Destroy(GameObject value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
