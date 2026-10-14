using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable ice cream machine model; its formula lives in Lua.</summary>
    public static class MResIceCreamMachineCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/IceCreamMachine.asset";
        [MenuItem("Tools/MRes/Create or Locate Ice Cream Machine Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "IceCreamMachineAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Ice cream machine authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/IceCreamMachineShaderRegistry.asset");
            var pink = Material(shader, "IceCreamMachinePink", new Color(.92f, .42f, .58f, 1), .55f);
            var cream = Material(shader, "IceCreamMachineCream", new Color(1f, .94f, .77f, 1), .7f);
            var mint = Material(shader, "IceCreamMachineMint", new Color(.3f, .82f, .7f, 1), .4f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "IceCreamMachineCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/IceCreamMachineCylinder.asset");
            var root = new GameObject("IceCreamMachine"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var hull = Visual(root, "Base", cylinder, pink, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f));
                var collider = hull.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "CreamBand", cylinder, cream, new Vector3(0, .13f, 0), new Vector3(.502f, .013f, .502f));
                for (int i = 0; i < 3; i++)
                {
                    float width = .26f - i * .065f;
                    Visual(root, "CreamTier" + i, cylinder, cream, new Vector3(0, .23f + i * .055f, 0), new Vector3(width, .03f, width));
                }
                Visual(root, "MintTop", cylinder, mint, new Vector3(0, .395f, 0), new Vector3(.055f, .025f, .055f));
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/IceCreamMachine.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/IceCreamMachine.lua");
            profile.Metadata = new MResMetadataData { name = "雪糕机", description = "每台每回合首次撞击提供×10倍率，之后每次有效触发递减1，依次×10、×9至×2，最低×1。每次累加倍率减1，到得分区统一计算；有增量时扣1耐久，降至×1后不生效、不扣耐久，新回合重置。原球正常反弹。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/IceCreamMachine.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/IceCreamMachine.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "IceCreamMachineAuthoring"); AssetDatabase.SaveAssets(); return profile;
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
        public static void CreateAndBuild() => Debug.Log("MRES_ICE_CREAM_MACHINE_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
