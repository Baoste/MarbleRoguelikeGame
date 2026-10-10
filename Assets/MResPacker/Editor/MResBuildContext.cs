using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialType = UnityEngine.PhysicsMaterial;
#else
using PhysicsMaterialType = UnityEngine.PhysicMaterial;
#endif

namespace MResPacker
{
    internal sealed class MResBuildContext
    {
        internal readonly Dictionary<Mesh, string> MeshPaths = new Dictionary<Mesh, string>();
        internal readonly Dictionary<Material, string> MaterialPaths = new Dictionary<Material, string>();
        internal readonly Dictionary<Texture2D, string> TexturePaths = new Dictionary<Texture2D, string>();
        internal readonly Dictionary<PhysicsMaterialType, string> PhysicsPaths = new Dictionary<PhysicsMaterialType, string>();
        internal readonly Dictionary<string, object> TextureSettings = new Dictionary<string, object>();
        internal readonly Dictionary<string, object> MeshNames = new Dictionary<string, object>();
        internal readonly Dictionary<Shader, string> Shaders;
        internal readonly MResAnalysisReport Report;
        private readonly HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
        internal MResBuildContext(Dictionary<Shader, string> shaders, MResAnalysisReport report) { Shaders = shaders; Report = report; }
        internal void Add(string path, MResEntryType type, byte[] bytes)
        {
            MResStructureValidator.ValidatePath(path);
            if (!paths.Add(path)) throw new ArgumentException("Duplicate package path: " + path);
            Report.Entries.Add(new MResEntryData(path, type, bytes));
        }
        internal string Mesh(Mesh mesh)
        {
            if (mesh == null) return null;
            if (MeshPaths.TryGetValue(mesh, out var existing)) return existing;
            string path = "meshes/" + MeshPaths.Count + ".meshbin";
            var bytes = MResMeshSerializer.Serialize(mesh);
            MeshPaths.Add(mesh, path); MeshNames.Add(path, mesh.name); Report.MeshCount++;
            Add(path, MResEntryType.Mesh, bytes); return path;
        }
        internal string Material(Material material)
        {
            if (material == null) return null;
            if (MaterialPaths.TryGetValue(material, out var existing)) return existing;
            string path = "materials/" + MaterialPaths.Count + ".json";
            var bytes = MResMaterialSerializer.Serialize(material, this);
            MaterialPaths.Add(material, path); Report.MaterialCount++;
            Add(path, MResEntryType.Material, bytes); return path;
        }
        internal string Texture(Texture2D texture)
        {
            if (texture == null) return null;
            if (TexturePaths.TryGetValue(texture, out var existing)) return existing;
            string path = "textures/" + TexturePaths.Count + ".png";
            var bytes = MResTextureSerializer.Serialize(texture, out var settings);
            TexturePaths.Add(texture, path); TextureSettings.Add(path, settings); Report.TextureCount++;
            Add(path, MResEntryType.Texture, bytes); return path;
        }
        internal string PhysicsMaterial(PhysicsMaterialType material)
        {
            if (material == null) return null;
            if (PhysicsPaths.TryGetValue(material, out var existing)) return existing;
            string path = "physics_materials/" + PhysicsPaths.Count + ".json";
            var bytes = MResJson.Bytes(MResJson.Object("name", material.name, "dynamicFriction", material.dynamicFriction,
                "staticFriction", material.staticFriction, "bounciness", material.bounciness,
                "frictionCombine", (int)material.frictionCombine, "bounceCombine", (int)material.bounceCombine));
            PhysicsPaths.Add(material, path); Report.PhysicsMaterialCount++;
            Add(path, MResEntryType.PhysicsMaterial, bytes); return path;
        }
        internal static object V2(Vector2 v) => MResJson.Object("x", v.x, "y", v.y);
        internal static object V3(Vector3 v) => MResJson.Object("x", v.x, "y", v.y, "z", v.z);
        internal static object V4(Vector4 v) => MResJson.Object("x", v.x, "y", v.y, "z", v.z, "w", v.w);
        internal static object Color(Color v) => MResJson.Object("r", v.r, "g", v.g, "b", v.b, "a", v.a);
    }
}
