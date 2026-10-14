using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable count-based multiplier model; its formula lives in Lua.</summary>
    public static class MResLessIsMoreCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/LessIsMore.asset";
        [MenuItem("Tools/MRes/Create or Locate Less Is More Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "LessIsMoreAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Less is more authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/LessIsMoreShaderRegistry.asset");
            var blue = Material(shader, "LessIsMorePurple", new Color(.22f, .09f, .36f, 1), .55f);
            var gold = Material(shader, "LessIsMoreSilver", new Color(.78f, .83f, .9f, 1), .7f);
            var cyan = Material(shader, "LessIsMoreOrange", new Color(.98f, .46f, .12f, 1), .4f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "LessIsMoreCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/LessIsMoreCylinder.asset");
            var root = new GameObject("LessIsMore"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var hull = Visual(root, "Base", cylinder, blue, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f));
                var collider = hull.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "SilverBand", cylinder, gold, new Vector3(0, .13f, 0), new Vector3(.502f, .013f, .502f));
                for (int i = 0; i < 3; i++)
                {
                    float height = .135f - i * .035f;
                    Visual(root, "Column" + i, cylinder, cyan, new Vector3((i - 1) * .135f, .2f + height / 2, 0), new Vector3(.085f, height / 2, .085f));
                    Visual(root, "Cap" + i, cylinder, gold, new Vector3((i - 1) * .135f, .2f + height, 0), new Vector3(.105f, .01f, .105f));
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/LessIsMore.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/LessIsMore.lua");
            profile.Metadata = new MResMetadataData { name = "less is more", description = "撞击时提供附加倍率，初始+20，每个其他已放置装置减少2，最低为0；不计自身，已耗尽但仍在场的装置也计数。倍率累加后到得分区统一计算。有增量时扣1耐久，其他装置达到10个时不生效，原球正常反弹。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/LessIsMore.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/LessIsMore.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "LessIsMoreAuthoring"); AssetDatabase.SaveAssets(); return profile;
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
        public static void CreateAndBuild() => Debug.Log("MRES_LESS_IS_MORE_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
