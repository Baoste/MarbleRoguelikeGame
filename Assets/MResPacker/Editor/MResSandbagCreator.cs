using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Authoring assets only; combo, timeout and bonus rules live in Sandbag.lua.</summary>
    public static class MResSandbagCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Sandbag.asset";
        [MenuItem("Tools/MRes/Create or Locate Sandbag Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "SandbagAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Sandbag authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/SandbagShaderRegistry.asset");
            var fabric = Material(shader, "SandbagCanvas", new Color(.72f, .48f, .23f, 1));
            var belt = Material(shader, "SandbagBand", new Color(.38f, .075f, .04f, 1));
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "SandbagCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/SandbagCylinder.asset");
            var root = new GameObject("Sandbag"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var visual = Visual(root, "Cylinder", cylinder, fabric, new Vector3(0, .1f, 0), new Vector3(.4f, .1f, .4f));
                var collider = visual.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "Band", cylinder, belt, new Vector3(0, .1f, 0), new Vector3(.401f, .015f, .401f));
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/Sandbag.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/Sandbag.lua");
            profile.Metadata = new MResMetadataData { name = "沙包", description = "撞击使连击数加1，并给弹珠增加连击数×1的基础价值；连击保留3/√连击数秒，最短0.3秒，超时清零。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Sandbag.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/Sandbag.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "SandbagAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Material Material(Shader shader, string name, Color color)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Smoothness", .08f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_SANDBAG_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
