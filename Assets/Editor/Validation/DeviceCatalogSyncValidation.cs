using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    // Batch validation uses the isolated project copy; restores JSON even if a check fails.
    public static class DeviceCatalogSyncValidation
    {
        private static string original;
        private static DeviceDefinition[] expected;
        private static double deadline;
        private static int assertions;
        [Serializable] private sealed class DeviceList { public DeviceDefinition[] Devices; }
        private static string Devices(DeviceDefinition[] devices) => JsonUtility.ToJson(new DeviceList { Devices = devices });
        private static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception("Device catalog sync: " + message); }
        private static string OtherContent(GameContent content)
        { content.Devices = null; return JsonUtility.ToJson(content); }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in a batch validation project.");
            try
            {
                assertions = 0;
                DeviceCatalogBuilder.Rebuild();
                expected = DeviceCatalogBuilder.ReadDefinitions();
                original = File.ReadAllText(DeviceCatalogBuilder.ContentPath);
                ValidateSnapshot();
                Check(DeviceCatalogBuilder.IsSyncInput(DeviceCatalogBuilder.ContentPath), "JSON edits queue synchronization");
                Check(DeviceCatalogBuilder.IsSyncInput("Assets/Resources/Devices/Sub/New.MRES"), "nested MRes edits queue synchronization");
                Check(!DeviceCatalogBuilder.IsSyncInput("Assets/Resources/Devices/DeviceCatalog.asset"), "generated catalog does not loop");
                // Verify the actual import callback, without explicitly rebuilding afterwards.
                var stale = JsonUtility.FromJson<GameContent>(original);
                stale.Devices = new[] { new DeviceDefinition { Id = uint.MaxValue, Name = "Obsolete JSON device", Price = 999 } };
                File.WriteAllText(DeviceCatalogBuilder.ContentPath, JsonUtility.ToJson(stale, true));
                AssetDatabase.ImportAsset(DeviceCatalogBuilder.ContentPath, ImportAssetOptions.ForceSynchronousImport);
                deadline = EditorApplication.timeSinceStartup + 15;
                EditorApplication.update += AwaitAutomaticSync;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void ValidateSnapshot()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/DeviceCatalogSyncValidation"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var content = JsonUtility.FromJson<GameContent>(original);
                string others = OtherContent(JsonUtility.FromJson<GameContent>(original));
                content.Devices = new[] { new DeviceDefinition { Id = uint.MaxValue, Name = "Old device", Price = 999 } };
                File.WriteAllText(path, JsonUtility.ToJson(content));
                Check(DeviceCatalogBuilder.SyncContentSnapshot(path, expected), "outdated snapshot is replaced");
                content = JsonUtility.FromJson<GameContent>(File.ReadAllText(path));
                Check(Devices(content.Devices) == Devices(expected), "exact current MRes definitions, no legacy rows");
                Check(OtherContent(content) == others, "attributes/content rules are preserved");
                string text = File.ReadAllText(path);
                Check(!DeviceCatalogBuilder.SyncContentSnapshot(path, expected) && text == File.ReadAllText(path), "unchanged sync does not rewrite");
                File.WriteAllText(path, text.Replace("\"StartingCount\":", "\"Radius\": 99, \"StartingCount\":"));
                Check(DeviceCatalogBuilder.SyncContentSnapshot(path, expected), "ignored legacy Radius still triggers snapshot cleanup");
                Check(!File.ReadAllText(path).Contains("\"Radius\""), "snapshot no longer stores placement radius");
                Check(OtherContent(JsonUtility.FromJson<GameContent>(File.ReadAllText(path))) == others, "radius cleanup preserves other content rules");
                var changed = expected.Skip(1).Select(d => d.Copy()).ToArray();
                changed[0].Price = 7; changed[0].SellPrice = 3; changed[0].Rarity = DeviceRarity.Epic;
                changed[0].Durability = -1; changed[0].StartingCount = 0;
                DeviceCatalogBuilder.SyncContentSnapshot(path, changed);
                content = JsonUtility.FromJson<GameContent>(File.ReadAllText(path));
                Check(Devices(content.Devices) == Devices(changed), "removed packages and changed metadata replace old rows");
                content.Devices = null; File.WriteAllText(path, JsonUtility.ToJson(content));
                DeviceCatalogBuilder.SyncContentSnapshot(path, expected);
                Check(Devices(JsonUtility.FromJson<GameContent>(File.ReadAllText(path)).Devices) == Devices(expected), "missing device array is restored");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        private static void AwaitAutomaticSync()
        {
            try
            {
                var content = JsonUtility.FromJson<GameContent>(File.ReadAllText(DeviceCatalogBuilder.ContentPath));
                if (Devices(content.Devices) == Devices(expected))
                {
                    Check(OtherContent(content) == OtherContent(JsonUtility.FromJson<GameContent>(original)), "automatic sync preserves unrelated content");
                    Finish(null);
                }
                else if (EditorApplication.timeSinceStartup > deadline) Finish(new Exception("GameContent import did not synchronize from MRes."));
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= AwaitAutomaticSync;
            if (original != null) File.WriteAllText(DeviceCatalogBuilder.ContentPath, original);
            if (error != null) Debug.LogException(error);
            else Debug.Log("DEVICE_CATALOG_SYNC_VALIDATION_PASSED: " + assertions + " assertions; full replacement, metadata, idempotence and automatic JSON import repair.");
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
