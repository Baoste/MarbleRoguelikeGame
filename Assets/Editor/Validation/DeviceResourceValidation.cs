using System;
using System.Linq;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static class DeviceResourceValidation
    {
        public static void Run()
        {
            var catalog = DeviceResourceCatalog.Current;
            var loadedIds = GameBalanceLoader.Load().Devices.Select(device => device.Id);
            var listedIds = ConfigJson.Read(DeviceResourceCatalog.LoadContentAsset().text, "GameContent.json")
                .Array("Devices").Select(value => (uint)(double)value).OrderBy(value => value);
            Check(loadedIds.SequenceEqual(listedIds), "game startup loads the listed IDs");
            var selection = DeviceResourceCatalog.Load("{\"Devices\":[9000]}");
            Check(selection.Definitions().Select(device => device.Id).SequenceEqual(new uint[] { 9000 }),
                "unlisted device folders are ignored");
            ExpectInvalidSelection("{\"Devices\":[9000,9000]}");
            ExpectInvalidSelection("{\"Devices\":[{\"Id\":9000}]}");
            ExpectInvalidSelection("{\"Devices\":[4294967295]}");

            var scene = SceneManager.CreateScene("DeviceResourceValidation", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var board = new GameObject("DeviceValidationBoard");
            SceneManager.MoveGameObjectToScene(board, scene);
            using var bridge = new MarblePhysicsBridge(scene, null, null, null, createVisuals: false);
            bridge.ConfigureDevicePlacement(board.transform);
            int scripts = DeviceLuaRuntime.ActiveBehaviourCount;
            int count = 0;
            try
            {
                var definitions = catalog.Definitions();
                foreach (var definition in definitions)
                {
                    var owned = new OwnedDeviceSnapshot { InstanceId = ++count, DefinitionId = definition.Id, Name = definition.Name };
                    using (var preview = DeviceView.CreatePreview(owned, board.transform))
                    {
                        Check(!preview.Root.activeSelf, "preview starts inactive");
                        preview.Root.SetActive(true);
                        Check(preview.Root.GetComponentsInChildren<Collider>(true).Length == 0, "preview contains only presentation nodes");
                        Check(preview.Root.GetComponent<DeviceBody>() == null, "preview cannot participate in gameplay");
                        Check(DeviceLuaRuntime.ActiveBehaviourCount == scripts, "preview does not create a Lua instance");
                    }
                    using (var script = catalog.Get(definition.Id).CreateScript())
                    {
                        Check(bridge.TrySetDevicePlacement(new DevicePlacement(owned.InstanceId, definition, 0, 0, 0),
                            (_, _) => script), "device physics can be installed without a presentation");
                        bridge.TryGetDeviceBody(owned.InstanceId, out var body);
                        Check(body.gameObject.scene == scene && body.isActiveAndEnabled, "device joins the correct physics scene");
                        Check(body.Script == script && body.GetComponentsInChildren<Renderer>(true).Length == 0,
                            "physical device binds only its session script");
                        var prefab = catalog.Get(definition.Id).Prefab;
                        var volume = prefab.transform.Find(DeviceResourceCatalog.PlacementVolumeName);
                        Check(body.GetComponentsInChildren<Collider>(true).Length ==
                            prefab.GetComponentsInChildren<Collider>(true).Count(collider => !collider.transform.IsChildOf(volume)),
                            "each authored gameplay collider is copied exactly once");
                        Check(body.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                            .All(c => c.sharedMaterial != null && Mathf.Approximately(c.sharedMaterial.bounciness, definition.Bounce)),
                            $"device {definition.Id} applies its configured bounce to solid colliders");
                        Check(script.HasFunction("OnContact") || script.HasFunction("OnField") || script.HasFunction("OnBallUpdate") ||
                            script.HasFunction("OnTick") || script.HasFunction("OnActivate"), "Lua asset handles gameplay");
                        bridge.RemoveDevice(owned.InstanceId);
                        Check(!script.IsDisposed, "physical removal preserves the owned device's Lua state");
                    }
                    Check(DeviceLuaRuntime.ActiveBehaviourCount == scripts, "script released after device disposal");
                }
                Debug.Log("DEVICE_RESOURCE_VALIDATION_PASSED: " + count + " prefabs, Lua assets, previews and device ownership.");
            }
            finally { UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
        private static void ExpectInvalidSelection(string json)
        {
            try { DeviceResourceCatalog.Load(json); }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException) { return; }
            throw new Exception("Invalid device selection was accepted: " + json);
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception("Device resource validation: " + message); }
    }
}
