using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable glutton bag model; its formula lives in Lua.</summary>
    public static class MResGluttonBagCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/GluttonBag.asset";
        [MenuItem("Tools/MRes/Create or Locate Glutton Bag Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "GluttonBagAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Glutton bag authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/GluttonBagShaderRegistry.asset");
            var olive = Material(shader, "GluttonBagOlive", new Color(.35f, .48f, .13f, 1), .55f);
            var tan = Material(shader, "GluttonBagTan", new Color(.86f, .7f, .36f, 1), .7f);
            var dark = Material(shader, "GluttonBagDark", new Color(.055f, .035f, .02f, 1), .4f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "GluttonBagCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/GluttonBagCylinder.asset");
            var root = new GameObject("GluttonBag"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var hull = Visual(root, "Base", cylinder, olive, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f));
                var collider = hull.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "MouthRim", cylinder, tan, new Vector3(0, .225f, 0), new Vector3(.38f, .025f, .38f));
                Visual(root, "Mouth", cylinder, dark, new Vector3(0, .257f, 0), new Vector3(.29f, .007f, .29f));
                for (int i = 0; i < 2; i++)
                {
                    Visual(root, "Tooth" + i, cylinder, tan, new Vector3((i * 2 - 1) * .105f, .27f, -.04f), new Vector3(.055f, .018f, .055f));
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/GluttonBag.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/GluttonBag.lua");
            profile.Metadata = new MResMetadataData { name = "大胃袋", description = "布置时将背包装置拖入，消耗该装置，按普通/稀有/史诗/传奇增加2/4/6/8点本回合附加倍率。弹珠碰撞时累加这些倍率增量，到得分区结算；初始无效果。喂食不扣耐久，有效碰撞扣1。成长仅用于即将开始的回合，下一轮布置清空，原球正常反弹。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/GluttonBag.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/GluttonBag.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "GluttonBagAuthoring"); AssetDatabase.SaveAssets(); return profile;
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
        public static void CreateAndBuild() => Debug.Log("MRES_GLUTTON_BAG_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
