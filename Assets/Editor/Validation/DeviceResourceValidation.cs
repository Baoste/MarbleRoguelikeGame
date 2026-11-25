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
            int scripts = DeviceLuaRuntime.ActiveBehaviourCount;
            int count = 0;
            try
            {
                var definitions = catalog.Definitions();
                foreach (var definition in definitions)
                {
                    var owned = new OwnedDeviceSnapshot { InstanceId = ++count, DefinitionId = definition.Id, Name = definition.Name };
                    using (var preview = DeviceView.Create(owned, board.transform, null, true))
                    {
                        Check(!preview.Root.activeSelf, "preview starts inactive");
                        preview.Root.SetActive(true);
                        Check(preview.Root.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "preview colliders disabled");
                        Check(preview.Root.GetComponent<DeviceBody>() == null, "preview cannot participate in gameplay");
                        Check(DeviceLuaRuntime.ActiveBehaviourCount == scripts, "preview does not create a Lua instance");
                    }
                    using (var script = catalog.Get(definition.Id).CreateScript())
                    {
                        using (var view = DeviceView.Create(owned, board.transform, script))
                        {
                            Check(view.Root.scene == scene && !view.Root.activeSelf, "device joins the correct physics scene before activation");
                            view.Root.SetActive(true);
                            Check(view.Root.GetComponent<DeviceBody>().Script == script, "view and gameplay share the same Lua instance");
                            Check(view.Root.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                                .All(c => c.sharedMaterial != null && Mathf.Approximately(c.sharedMaterial.bounciness, definition.Bounce)),
                                $"device {definition.Id} applies its configured bounce to solid colliders");
                            Check(script.HasFunction("OnContact") || script.HasFunction("OnField") || script.HasFunction("OnBallUpdate") ||
                                script.HasFunction("OnTick") || script.HasFunction("OnActivate"), "Lua asset handles gameplay");
                        }
                        Check(!script.IsDisposed, "visual disposal preserves the session script");
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
