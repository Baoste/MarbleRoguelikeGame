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
        public int callbackOrder => 0;
        static DeviceCatalogBuilder()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                try { Rebuild(); }
                catch (Exception e) { EditorApplication.isPlaying = false; Debug.LogException(e); }
            };
        }
        public void OnPreprocessBuild(BuildReport report) => Rebuild();
        [MenuItem("Tools/MRes/Rebuild Device Catalog")]
        public static void Rebuild()
        {
            if (!Directory.Exists(Folder)) throw new BuildFailedException("Missing " + Folder);
            var entries = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".mres", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new DeviceResourceCatalog.Entry { File = p.Replace('\\', '/'), Bytes = File.ReadAllBytes(p) }).ToArray();
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
                // Compatibility snapshot for the existing offline balance/workbook tools.
                // Gameplay always obtains these definitions from the MRes catalog instead.
                var contentPath = "Assets/Resources/GameContent.json";
                var content = JsonUtility.FromJson<GameContent>(File.ReadAllText(contentPath));
                if (JsonUtility.ToJson(new DefinitionList { Devices = content.Devices }) !=
                    JsonUtility.ToJson(new DefinitionList { Devices = definitions }))
                {
                    content.Devices = definitions;
                    File.WriteAllText(contentPath, JsonUtility.ToJson(content, true) + "\n");
                    AssetDatabase.ImportAsset(contentPath);
                }
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
        [Serializable] private sealed class DefinitionList { public DeviceDefinition[] Devices; }
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] from)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(from).Any(p =>
                p.StartsWith(Folder + "/", StringComparison.OrdinalIgnoreCase) &&
                (p.EndsWith(".mres", StringComparison.OrdinalIgnoreCase) || p.EndsWith("ShaderRegistry.asset", StringComparison.Ordinal))))
                EditorApplication.delayCall += Refresh;
        }
        private static void Refresh()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Refresh; return; }
            try { Rebuild(); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
