using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable pig bank model; the wallet formula and repeated impacts live in Lua.</summary>
    public static class MResPiggyBankCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/PiggyBank.asset";
        [MenuItem("Tools/MRes/Create or Locate Piggy Bank Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "PiggyBankAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Piggy bank authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/PiggyBankShaderRegistry.asset");
            var pink = Material(shader, "PiggyBankPink", new Color(1, .48f, .62f, 1));
            var rose = Material(shader, "PiggyBankRose", new Color(.74f, .21f, .35f, 1));
            var dark = Material(shader, "PiggyBankDark", new Color(.055f, .025f, .035f, 1));
            var sphere = Mesh(PrimitiveType.Sphere, "PiggyBankSphere"); var cube = Mesh(PrimitiveType.Cube, "PiggyBankCube");
            var root = new GameObject("PiggyBank"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Body", sphere, pink, new Vector3(0, .2f, 0), new Vector3(.46f, .33f, .53f));
                Visual(root, "Snout", sphere, rose, new Vector3(0, .21f, -.26f), new Vector3(.2f, .13f, .1f));
                Visual(root, "CoinSlot", cube, dark, new Vector3(0, .365f, .005f), new Vector3(.15f, .008f, .022f));
                foreach (int side in new[] { -1, 1 })
                {
                    Visual(root, "Nostril" + side, sphere, dark, new Vector3(side * .04f, .215f, -.307f), new Vector3(.022f, .03f, .012f));
                    Visual(root, "Eye" + side, sphere, dark, new Vector3(side * .105f, .275f, -.205f), Vector3.one * .033f);
                    var ear = Visual(root, "Ear" + side, cube, pink, new Vector3(side * .12f, .365f, -.105f), new Vector3(.075f, .105f, .045f));
                    ear.transform.localRotation = Quaternion.Euler(15, 0, side * -22);
                    foreach (int end in new[] { -1, 1 })
                        Visual(root, "Foot" + side + "_" + end, sphere, rose, new Vector3(side * .125f, .045f, end * .13f), new Vector3(.095f, .09f, .11f));
                }
                Visual(root, "Tail", sphere, rose, new Vector3(0, .23f, .273f), new Vector3(.07f, .065f, .07f));
                var collider = root.AddComponent<CapsuleCollider>(); collider.direction = 2;
                collider.center = new Vector3(0, .185f, 0); collider.radius = .185f; collider.height = .56f;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/PiggyBank.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/PiggyBank.lua");
            profile.Metadata = new MResMetadataData { name = "猪猪存钱罐", description = "每次撞击按当前金币提供1+0.1×满10金币档数的倍率；每次只累加倍率-1，最终倍率为1+累计增量，到得分区统一计算。不消耗金币，金币不足10时不生效、不扣耐久。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/PiggyBank.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/PiggyBank.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "PiggyBankAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Mesh Mesh(PrimitiveType type, string name)
        {
            var primitive = GameObject.CreatePrimitive(type); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = name; AssetDatabase.CreateAsset(mesh, MResExampleCreator.Root + "/Models/" + name + ".asset"); return mesh;
        }
        private static Material Material(Shader shader, string name, Color color)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Smoothness", .6f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false); result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_PIGGY_BANK_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
