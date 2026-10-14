using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable count-based multiplier model; its formula lives in Lua.</summary>
    public static class MResMoreIsMoreCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/MoreIsMore.asset";
        [MenuItem("Tools/MRes/Create or Locate More Is More Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "MoreIsMoreAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("More is more authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/MoreIsMoreShaderRegistry.asset");
            var blue = Material(shader, "MoreIsMoreBlue", new Color(.065f, .24f, .42f, 1), .55f);
            var gold = Material(shader, "MoreIsMoreGold", new Color(.94f, .65f, .13f, 1), .7f);
            var cyan = Material(shader, "MoreIsMoreCyan", new Color(.1f, .78f, .77f, 1), .4f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "MoreIsMoreCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/MoreIsMoreCylinder.asset");
            var root = new GameObject("MoreIsMore"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var hull = Visual(root, "Base", cylinder, blue, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f));
                var collider = hull.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "GoldBand", cylinder, gold, new Vector3(0, .13f, 0), new Vector3(.502f, .013f, .502f));
                for (int i = 0; i < 3; i++)
                {
                    float height = .065f + i * .035f;
                    Visual(root, "Column" + i, cylinder, cyan, new Vector3((i - 1) * .135f, .2f + height / 2, 0), new Vector3(.085f, height / 2, .085f));
                    Visual(root, "Cap" + i, cylinder, gold, new Vector3((i - 1) * .135f, .2f + height, 0), new Vector3(.105f, .01f, .105f));
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/MoreIsMore.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/MoreIsMore.lua");
            profile.Metadata = new MResMetadataData { name = "more is more", description = "撞击时每个其他已放置装置提供+2附加倍率，单次最多+20；不计自身，已耗尽但仍在场的装置也计数。倍率累加后到得分区统一计算。有增量时扣1耐久，无其他装置不生效，原球正常反弹。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/MoreIsMore.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/MoreIsMore.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "MoreIsMoreAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Material Material(Shader shader, string name, Color color, float metallic)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", .4f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_MORE_IS_MORE_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
