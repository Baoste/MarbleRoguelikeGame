using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable presentation only; charge, radial layout and cooldown live in StoneBurst.lua.</summary>
    public static class MResStoneBurstCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/StoneBurst.asset";
        [MenuItem("Tools/MRes/Create or Locate Stone Burst Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "StoneBurstAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("StoneBurst authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/StoneBurstShaderRegistry.asset");
            var stone = Material(shader, "StoneBurstSlate", new Color(.3f, .35f, .4f, 1), .05f);
            var band = Material(shader, "StoneBurstEmber", new Color(1, .3f, .035f, 1), .3f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "StoneBurstCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/StoneBurstCylinder.asset");
            var root = new GameObject("StoneBurst"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var visual = Visual(root, "Stone", cylinder, stone, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f));
                var collider = visual.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "ChargeBand", cylinder, band, new Vector3(0, .1f, 0), new Vector3(.501f, .012f, .501f));
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/StoneBurst.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/StoneBurst.lua");
            profile.Metadata = new MResMetadataData { name = "大石碎胸口", description = "受到8次撞击后，向四周均匀发射16颗价值1、速度6的弹珠；冷却5秒后重新蓄力。每轮发射扣1耐久，自身发出的球不为自身蓄力。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/StoneBurst.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/StoneBurst.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "StoneBurstAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Material Material(Shader shader, string name, Color color, float smoothness)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_STONE_BURST_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
