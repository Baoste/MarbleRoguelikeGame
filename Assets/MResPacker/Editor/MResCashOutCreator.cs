using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable coin exchange gate; branching and payout are implemented in packaged Lua.</summary>
    public static class MResCashOutCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/CashOut.asset";
        [MenuItem("Tools/MRes/Create or Locate Cash Out Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "CashOutAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Cash out authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/CashOutShaderRegistry.asset");
            var gold = Material(shader, "CashOutGold", new Color(1, .68f, .1f, 1), .65f);
            var dark = Material(shader, "CashOutDark", new Color(.09f, .13f, .12f, 1), .2f);
            var jade = Material(shader, "CashOutJade", new Color(.13f, .65f, .42f, 1), .25f);
            var cube = Mesh(PrimitiveType.Cube, "CashOutCube"); var cylinder = Mesh(PrimitiveType.Cylinder, "CashOutCylinder");
            var root = new GameObject("CashOut"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Base", cube, dark, new Vector3(0, .025f, 0), new Vector3(.49f, .05f, .35f));
                foreach (int side in new[] { -1, 1 })
                    Visual(root, "Pillar" + side, cube, gold, new Vector3(side * .215f, .18f, 0), new Vector3(.055f, .3f, .14f));
                Visual(root, "Lintel", cube, jade, new Vector3(0, .325f, 0), new Vector3(.49f, .055f, .14f));
                var coin = Visual(root, "Coin", cylinder, gold, new Vector3(0, .36f, 0), new Vector3(.15f, .018f, .15f));
                coin.transform.localRotation = Quaternion.Euler(90, 0, 0);
                Visual(root, "CoinMark", cube, dark, new Vector3(0, .36f, -.02f), new Vector3(.022f, .09f, .008f));
                Visual(root, "ExitArrow", cube, jade, new Vector3(0, .057f, -.12f), new Vector3(.045f, .009f, .12f));
                var trigger = root.AddComponent<BoxCollider>(); trigger.isTrigger = true;
                trigger.center = new Vector3(0, .16f, 0); trigger.size = new Vector3(.38f, .26f, .24f);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/CashOut.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/CashOut.lua");
            profile.Metadata = new MResMetadataData { name = "赚赚", description = "弹珠进入时，若此前成功触发过其他装置实例，则销毁并返还四舍五入后的当前价值×累计装置倍率金币；否则保持速度大小从棋盘下方射出。每次成功处理扣1耐久，不计回合分数。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/CashOut.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/CashOut.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "CashOutAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Mesh Mesh(PrimitiveType type, string name)
        {
            var primitive = GameObject.CreatePrimitive(type); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = name; AssetDatabase.CreateAsset(mesh, MResExampleCreator.Root + "/Models/" + name + ".asset"); return mesh;
        }
        private static Material Material(Shader shader, string name, Color color, float metallic)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", .45f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false); result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_CASH_OUT_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
