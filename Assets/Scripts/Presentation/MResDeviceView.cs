using System;
using MRes;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Presentation
{
    public static class MResDeviceView
    {
        /// <summary>Returns an inactive root. Caller sets its board pose before activating.</summary>
        public static MResLoadHandle Create(OwnedDeviceSnapshot device, Transform parent, MResLuaScript script, bool preview = false)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            var catalog = DeviceResourceCatalog.Current;
            var resource = catalog.Get(device.DefinitionId);
            var handle = MResLoader.Load(resource.Package, catalog.Shaders,
                new MResLoadOptions { ExecuteLua = !preview, ActivateRoot = false, SharedScript = script });
            try
            {
                var root = handle.Root;
                SceneManager.MoveGameObjectToScene(root, parent.gameObject.scene);
                root.transform.SetParent(parent, false);
                root.name = device.Name + (preview ? " Preview" : " #" + device.InstanceId);
                if (preview)
                {
                    foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
                }
                else
                {
                    if (script == null) throw new ArgumentNullException(nameof(script));
                    root.AddComponent<MResDeviceBody>().Initialize(device.InstanceId, resource.Definition, script);
                    if (!string.IsNullOrEmpty(resource.Definition.LinkGroup)) root.AddComponent<DeviceLinkView>();
                }
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
    }
}
