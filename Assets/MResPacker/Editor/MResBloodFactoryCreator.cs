using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable factory assets; value conversion and healing formula live in Lua.</summary>
    public static class MResBloodFactoryCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/BloodFactory.asset";
        [MenuItem("Tools/MRes/Create or Locate Blood Factory Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "BloodFactoryAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Blood factory authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/BloodFactoryShaderRegistry.asset");
            var stone = Material(shader, "BloodFactoryRed", new Color(.65f, .06f, .12f, 1), .35f, .5f);
            var gold = Material(shader, "BloodFactoryIvory", new Color(.9f, .92f, .88f, 1), .5f, .4f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "BloodFactoryCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/BloodFactoryCylinder.asset");
            var root = new GameObject("BloodFactory"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var visual = Visual(root, "Tank", cylinder, stone, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f));
                var collider = visual.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "TankBand", cylinder, gold, new Vector3(0, .12f, 0), new Vector3(.501f, .012f, .501f));
                Visual(root, "Chimney", cylinder, gold, new Vector3(-.08f, .28f, -.035f), new Vector3(.09f, .08f, .09f));
                Visual(root, "Valve", cylinder, gold, new Vector3(.09f, .205f, .035f), new Vector3(.1f, .027f, .13f));
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/BloodFactory.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/BloodFactory.lua");
            profile.Metadata = new MResMetadataData { name = "造血工厂", description = "弹珠撞击后价值减半并向下取整，回复减半后价值×5的血量（不超过血量上限），不消耗弹珠。每次处理扣1耐久；价值已为0不触发。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/BloodFactory.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/BloodFactory.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "BloodFactoryAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Material Material(Shader shader, string name, Color color, float metallic, float smoothness)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_BLOOD_FACTORY_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
