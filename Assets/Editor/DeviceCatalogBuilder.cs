using System;
using System.IO;
using System.Linq;
using MRes;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MarblesECS.Editor
{
    [InitializeOnLoad]
    public sealed class DeviceCatalogBuilder : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private const string Folder = "Assets/Resources/Devices";
        private const string CatalogPath = Folder + "/DeviceCatalog.asset";
        internal const string ContentPath = "Assets/Resources/GameContent.json";
        private static bool refreshQueued;
        public int callbackOrder => 0;
        static DeviceCatalogBuilder()
        {
            QueueRefresh();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) QueueRefresh();
                if (state != PlayModeStateChange.ExitingEditMode) return;
                try { Rebuild(); }
                catch (Exception e) { EditorApplication.isPlaying = false; Debug.LogException(e); }
            };
        }
        public void OnPreprocessBuild(BuildReport report) => Rebuild();
        [MenuItem("Tools/MRes/Rebuild Device Catalog")]
        public static void Rebuild()
        {
            var entries = ReadEntries();
            var registry = AssetDatabase.LoadAssetAtPath<MResShaderRegistry>(Folder + "/ShaderRegistry.asset");
            if (registry == null) throw new BuildFailedException("Missing explicit device ShaderRegistry.asset.");
            var validation = ScriptableObject.CreateInstance<DeviceResourceCatalog>();
            try
            {
                validation.Entries = entries; validation.Shaders = registry;
                var definitions = validation.Definitions();
                foreach (var definition in definitions)
                    using (MResLoader.Load(validation.Get(definition.Id).Package, registry,
                        new MResLoadOptions { ExecuteLua = false, ActivateRoot = false })) { }
                // Replace the entire snapshot; never merge stale JSON/Excel device rows.
                if (SyncContentSnapshot(ContentPath, definitions)) AssetDatabase.ImportAsset(ContentPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(validation); }
            var catalog = AssetDatabase.LoadAssetAtPath<DeviceResourceCatalog>(CatalogPath);
            if (catalog != null && catalog.Shaders == registry && catalog.Entries.Length == entries.Length &&
                catalog.Entries.Zip(entries, (a, b) => a.File == b.File && a.Bytes.SequenceEqual(b.Bytes)).All(same => same)) return;
            if (catalog == null)
            { catalog = ScriptableObject.CreateInstance<DeviceResourceCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            catalog.Entries = entries; catalog.Shaders = registry; catalog.Invalidate();
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        }
        private static DeviceResourceCatalog.Entry[] ReadEntries()
        {
            if (!Directory.Exists(Folder)) throw new BuildFailedException("Missing " + Folder);
            return Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".mres", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new DeviceResourceCatalog.Entry { File = p.Replace('\\', '/'), Bytes = File.ReadAllBytes(p) }).ToArray();
        }
        // Excel imports also read the current files, not a possibly outdated in-memory catalog.
        internal static DeviceDefinition[] ReadDefinitions()
        {
            var catalog = ScriptableObject.CreateInstance<DeviceResourceCatalog>();
            try { catalog.Entries = ReadEntries(); return catalog.Definitions(); }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }
        internal static bool SyncContentSnapshot(string path, DeviceDefinition[] definitions)
        {
            string source = File.ReadAllText(path);
            var content = JsonUtility.FromJson<GameContent>(source);
            if (content == null) throw new InvalidDataException("GameContent.json is empty.");
            if (JsonUtility.ToJson(new DefinitionList { Devices = content.Devices }) ==
                JsonUtility.ToJson(new DefinitionList { Devices = definitions }) &&
                // JsonUtility ignores the removed device field; still rewrite old snapshots once to remove it.
                !source.Contains("\"Radius\"")) return false;
            content.Devices = definitions;
            BalanceAssetWriter.Write(path, JsonUtility.ToJson(content, true) + "\n");
            return true;
        }
        [Serializable] private sealed class DefinitionList { public DeviceDefinition[] Devices; }
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] from)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(from).Any(IsSyncInput)) QueueRefresh();
        }
        internal static bool IsSyncInput(string path) =>
            string.Equals(path, ContentPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, Folder, StringComparison.OrdinalIgnoreCase) ||
            (path.StartsWith(Folder + "/", StringComparison.OrdinalIgnoreCase) &&
                (path.EndsWith(".mres", StringComparison.OrdinalIgnoreCase) || path.EndsWith("ShaderRegistry.asset", StringComparison.OrdinalIgnoreCase)));
        private static void QueueRefresh()
        {
            if (refreshQueued) return;
            refreshQueued = true;
            EditorApplication.delayCall += Refresh;
        }
        private static void Refresh()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Refresh; return; }
            refreshQueued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try { Rebuild(); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
