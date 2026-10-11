using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable mine assets; value loss, coin payout and round limit live in Lua.</summary>
    public static class MResGoldMineCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/GoldMine.asset";
        [MenuItem("Tools/MRes/Create or Locate Gold Mine Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "GoldMineAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Gold mine authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/GoldMineShaderRegistry.asset");
            var stone = Material(shader, "GoldMineStone", new Color(.28f, .25f, .2f, 1), 0, .1f);
            var gold = Material(shader, "GoldMineGold", new Color(1, .66f, .07f, 1), .65f, .4f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "GoldMineCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/GoldMineCylinder.asset");
            var root = new GameObject("GoldMine"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var visual = Visual(root, "OreBody", cylinder, stone, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f));
                var collider = visual.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "GoldVein", cylinder, gold, new Vector3(0, .12f, 0), new Vector3(.501f, .012f, .501f));
                Visual(root, "GoldNuggetA", cylinder, gold, new Vector3(-.08f, .205f, -.035f), new Vector3(.13f, .02f, .1f));
                Visual(root, "GoldNuggetB", cylinder, gold, new Vector3(.09f, .205f, .035f), new Vector3(.1f, .027f, .13f));
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/GoldMine.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/GoldMine.lua");
            profile.Metadata = new MResMetadataData { name = "矿老板", description = "弹珠每次撞击基础价值-2（最低为0），金币+4；每座矿每回合最多产出20金币，每次产出扣1耐久。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/GoldMine.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/GoldMine.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "GoldMineAuthoring"); AssetDatabase.SaveAssets(); return profile;
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
        public static void CreateAndBuild() => Debug.Log("MRES_GOLD_MINE_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
