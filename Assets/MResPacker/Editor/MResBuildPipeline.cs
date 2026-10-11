using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    [Serializable]
    public sealed class MResAnalysisReport
    {
        public int NodeCount, MeshCount, MaterialCount, TextureCount, PhysicsMaterialCount, ColliderCount, RigidbodyCount;
        public bool HasLua;
        public long EstimatedSize;
        public string Metadata;
        public readonly List<string> ShaderIds = new List<string>();
        public readonly List<string> UnsupportedComponents = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public bool CanBuild => Errors.Count == 0 && UnsupportedComponents.Count == 0;
        internal readonly List<MResEntryData> Entries = new List<MResEntryData>();
        public override string ToString() => $"Nodes {NodeCount}, Meshes {MeshCount}, Materials {MaterialCount}, Textures {TextureCount}, Physics materials {PhysicsMaterialCount}, Colliders {ColliderCount}, Rigidbodies {RigidbodyCount}, Lua {HasLua}, Size {EstimatedSize} bytes\n" +
            "Shader IDs: " + string.Join(", ", ShaderIds) + "\n" + Metadata + "\n" + string.Join("\n", Errors.Concat(UnsupportedComponents));
    }
    public static class MResBuildPipeline
    {
        public static void BuildAuthoringProfiles()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:MResBuildProfile", new[] { "Assets/MResPacker/Authoring" }))
            {
                var profile = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(AssetDatabase.GUIDToAssetPath(guid));
                Build(profile); EditorUtility.SetDirty(profile); count++;
            }
            AssetDatabase.SaveAssets(); Debug.Log("MRES_AUTHORING_BUILD_PASSED: " + count + " profiles.");
        }
        public static MResAnalysisReport Analyze(MResBuildProfile profile)
        {
            var report = new MResAnalysisReport();
            try
            {
                if (profile == null) throw new ArgumentException("Select a build profile.");
                report.HasLua = profile.LuaScript != null;
                if (profile.Metadata == null) throw new ArgumentException("Metadata is required.");
                profile.Metadata.Validate(); report.Metadata = JsonUtility.ToJson(profile.Metadata);
                MResPrefabValidator.Validate(profile.SourcePrefab, report);
                if (!report.CanBuild) return report;
                if (profile.ShaderRegistry == null) throw new ArgumentException("Select a shader registry.");
                var context = new MResBuildContext(profile.ShaderRegistry.ValidateAndMap(), report);
                var prefab = MResPrefabSerializer.Serialize(profile.SourcePrefab, context);
                context.Add("prefab/root.json", MResEntryType.Prefab, MResJson.Bytes(prefab));
                context.Add("metadata/metadata.json", MResEntryType.Metadata, MResJson.Bytes(MResJson.Object(
                    "name", profile.Metadata.name, "description", profile.Metadata.description, "value", profile.Metadata.value, "type", profile.Metadata.type,
                    "purchasePrice", profile.Metadata.purchasePrice, "rarity", profile.Metadata.rarity, "durability", profile.Metadata.durability)));
                string lua = null;
                if (profile.LuaScript != null)
                {
                    string path = AssetDatabase.GetAssetPath(profile.LuaScript);
                    if (!path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                        throw new ArgumentException("Lua Script must be an existing UTF-8 .lua asset.");
                    string source = MResJson.Utf8.GetString(File.ReadAllBytes(path));
                    if (source.StartsWith("\uFEFF", StringComparison.Ordinal)) source = source.Substring(1);
                    lua = "scripts/main.lua"; context.Add(lua, MResEntryType.Lua, MResJson.Utf8.GetBytes(source)); report.HasLua = true;
                }
                foreach (var extra in profile.ExtraFiles ?? Array.Empty<MResExtraFile>())
                {
                    if (extra == null || extra.Source == null) throw new ArgumentException("Extra file has no source.");
                    string extension = Path.GetExtension(extra.VirtualPath ?? "").ToLowerInvariant();
                    if (new[] { ".prefab", ".meta", ".mat", ".fbx", ".obj", ".shader", ".hlsl", ".cginc", ".compute" }.Contains(extension))
                        throw new ArgumentException("Raw Unity/model/shader assets are not valid extra entries: " + extra.VirtualPath);
                    context.Add(extra.VirtualPath, MResEntryType.Other, extra.Source.bytes);
                }
                var manifest = MResJson.Object("format", "mres", "version", 1, "packageName", profile.SourcePrefab.name,
                    "prefab", "prefab/root.json", "metadata", "metadata/metadata.json",
                    "meshes", context.MeshPaths.Values.ToArray(), "materials", context.MaterialPaths.Values.ToArray(),
                    "textures", context.TexturePaths.Values.ToArray(), "physicsMaterials", context.PhysicsPaths.Values.ToArray(),
                    "textureSettings", context.TextureSettings, "meshNames", context.MeshNames);
                if (lua != null) manifest.Add("lua", lua);
                context.Add("manifest.json", MResEntryType.Manifest, MResJson.Bytes(manifest));
                report.EstimatedSize = MResWriter.Estimate(report.Entries);
                if (report.EstimatedSize > MResWriter.MaxPackageBytes) throw new ArgumentException("Package exceeds 256 MiB.");
                report.ShaderIds.Sort(StringComparer.Ordinal);
            }
            catch (Exception error) { report.Errors.Add(error.Message); }
            return report;
        }
        public static MResAnalysisReport Build(MResBuildProfile profile)
        {
            var report = Analyze(profile);
            if (!report.CanBuild) throw new InvalidOperationException(report.ToString());
            MResWriter.Write(profile.OutputPath, report.Entries);
            var full = Path.GetFullPath(profile.OutputPath).Replace('\\', '/');
            var assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/') + "/";
            if (full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) AssetDatabase.ImportAsset("Assets/" + full.Substring(assets.Length));
            return report;
        }
    }
}
