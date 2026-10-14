using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable malfunction model; random outcomes are entirely authored in Lua.</summary>
    public static class MResMalfunctionCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Malfunction.asset";
        [MenuItem("Tools/MRes/Create or Locate Malfunction Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "MalfunctionAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Malfunction authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/MalfunctionShaderRegistry.asset");
            var metal = Material(shader, "MalfunctionMetal", new Color(.22f, .25f, .29f, 1), .7f);
            var warning = Material(shader, "MalfunctionWarning", new Color(.95f, .56f, .045f, 1), .3f);
            var red = Material(shader, "MalfunctionRed", new Color(.75f, .035f, .045f, 1), .2f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "MalfunctionCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/MalfunctionCylinder.asset");
            var root = new GameObject("Malfunction"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var hull = Visual(root, "Hull", cylinder, metal, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f));
                var collider = hull.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "WarningBand", cylinder, warning, new Vector3(0, .115f, 0), new Vector3(.502f, .018f, .502f));
                Visual(root, "Alarm", cylinder, red, new Vector3(0, .215f, 0), new Vector3(.14f, .025f, .14f));
                var rod = Visual(root, "BrokenRod", cylinder, metal, new Vector3(.13f, .235f, .045f), new Vector3(.045f, .08f, .045f));
                rod.transform.localRotation = Quaternion.Euler(0, 0, -28);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/Malfunction.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/Malfunction.lua");
            profile.Metadata = new MResMetadataData { name = "故障装置", description = "碰撞后等概率抽取0、1、2、3、5、7、11、13倍率。0将当前累计装置倍率归零；其他结果累加倍率减1，归零后仍可继续增加，到得分区统一计算。每次有效碰撞扣1耐久，原球正常反弹。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Malfunction.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/Malfunction.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "MalfunctionAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Material Material(Shader shader, string name, Color color, float metallic)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", .35f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_MALFUNCTION_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
