using System;
using System.IO;
using System.Linq;
using MRes;
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
            var scene = SceneManager.CreateScene("MResDeviceResourceValidation", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var board = new GameObject("DeviceValidationBoard");
            SceneManager.MoveGameObjectToScene(board, scene);
            int scripts = MResLuaRuntime.ActiveBehaviourCount;
            int count = 0;
            try
            {
                var snapshot = JsonUtility.FromJson<GameContent>(File.ReadAllText(DeviceCatalogBuilder.ContentPath));
                var definitions = catalog.Definitions();
                Check(snapshot.Devices != null && snapshot.Devices.Length == definitions.Length &&
                    snapshot.Devices.Zip(definitions, (a, b) => JsonUtility.ToJson(a) == JsonUtility.ToJson(b)).All(same => same),
                    "GameContent device snapshot equals the current MRes catalog");
                foreach (var entry in catalog.Entries)
                    Check(entry.Bytes.SequenceEqual(File.ReadAllBytes(entry.File)), "catalog equals raw package " + entry.File);
                foreach (var definition in catalog.Definitions())
                {
                    var owned = new OwnedDeviceSnapshot { InstanceId = ++count, DefinitionId = definition.Id, Name = definition.Name };
                    using (var preview = MResDeviceView.Create(owned, board.transform, null, true))
                    {
                        Check(!preview.Root.activeSelf, "preview starts inactive");
                        preview.Root.SetActive(true);
                        Check(preview.Root.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "preview colliders disabled");
                        Check(preview.Root.GetComponent<MResDeviceBody>() == null, "preview cannot participate in gameplay");
                        Check(MResLuaRuntime.ActiveBehaviourCount == scripts, "preview does not create a Lua instance");
                    }
                    using (var script = new MResLuaScript(catalog.Get(definition.Id).Package))
                    {
                        using (var view = MResDeviceView.Create(owned, board.transform, script))
                        {
                            Check(view.Root.scene == scene && !view.Root.activeSelf, "device joins the correct physics scene before activation");
                            view.Root.SetActive(true);
                            Check(view.Root.GetComponent<MResDeviceBody>().Script == script, "view and gameplay share the same Lua instance");
                            Check(script.HasFunction("OnContact") || script.HasFunction("OnField") || script.HasFunction("OnBallUpdate"), "packaged Lua handles gameplay");
                        }
                        Check(!script.IsDisposed, "visual disposal preserves the session script");
                    }
                    Check(MResLuaRuntime.ActiveBehaviourCount == scripts, "script released after device disposal");
                }
                Check(count == catalog.Entries.Length && definitions.Select(d => d.Id).Distinct().Count() == count, "current packages define the complete device list");
                Check(!Directory.Exists("Assets/Prefabs/Devices"), "old device prefabs removed");
                Debug.Log("DEVICE_RESOURCE_VALIDATION_PASSED: " + count + " raw packages, previews, scene placement and ownership.");
            }
            finally { UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception("MRes device validation: " + message); }
    }
}
