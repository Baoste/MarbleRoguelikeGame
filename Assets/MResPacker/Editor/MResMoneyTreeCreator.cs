using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable tree with a non-solid counting region. All reward rules live in Lua.</summary>
    public static class MResMoneyTreeCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/MoneyTree.asset";
        [MenuItem("Tools/MRes/Create or Locate Money Tree Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "MoneyTreeAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Money tree authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/MoneyTreeShaderRegistry.asset");
            var gold = Material(shader, "MoneyTreeGold", new Color(1, .7f, .08f, 1), .65f);
            var bark = Material(shader, "MoneyTreeBark", new Color(.3f, .15f, .055f, 1), 0);
            var leaves = Material(shader, "MoneyTreeLeaves", new Color(.23f, .62f, .18f, 1), .1f);
            var sphere = Mesh(PrimitiveType.Sphere, "MoneyTreeSphere");
            var cylinder = Mesh(PrimitiveType.Cylinder, "MoneyTreeCylinder");
            var root = new GameObject("MoneyTree"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "RootCoin", cylinder, gold, new Vector3(0, .025f, 0), new Vector3(.46f, .025f, .46f));
                Visual(root, "Trunk", cylinder, bark, new Vector3(0, .25f, 0), new Vector3(.09f, .23f, .09f));
                Visual(root, "Crown", sphere, leaves, new Vector3(0, .58f, 0), new Vector3(.55f, .38f, .5f));
                foreach (int side in new[] { -1, 1 })
                {
                    var branch = Visual(root, "Branch" + side, cylinder, bark, new Vector3(side * .11f, .37f, 0), new Vector3(.055f, .13f, .055f));
                    branch.transform.localRotation = Quaternion.Euler(0, 0, side * -45);
                    Visual(root, "Leaves" + side, sphere, leaves, new Vector3(side * .19f, .48f, 0), new Vector3(.3f, .28f, .34f));
                }
                for (int i = 0; i < 5; i++)
                {
                    float angle = i * Mathf.PI * 2 / 5;
                    var coin = Visual(root, "FruitCoin" + i, cylinder, gold,
                        new Vector3(Mathf.Sin(angle) * .22f, .54f + (i % 2) * .1f, Mathf.Cos(angle) * .22f), new Vector3(.13f, .012f, .13f));
                    coin.transform.localRotation = Quaternion.Euler(90, i * 72, 0);
                }
                // Even the trunk and coins are purely visual: only this trigger participates in physics.
                var trigger = root.AddComponent<BoxCollider>(); trigger.isTrigger = true;
                trigger.center = new Vector3(0, .15f, 0); trigger.size = new Vector3(.7f, .3f, .7f);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/MoneyTree.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/MoneyTree.lua");
            profile.Metadata = new MResMetadataData { name = "摇钱树", description = "无实体碰撞，弹珠每次进入区域计1次，离开后重入可重复计数；每累计10次发放5金币并扣1耐久。弹珠正常穿过，不消耗、不改价值或速度，计数每回合清零。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/MoneyTree.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/MoneyTree.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "MoneyTreeAuthoring"); AssetDatabase.SaveAssets(); return profile;
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
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", .4f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false); result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_MONEY_TREE_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
