using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable turf model; collision pairing, timing and output formula live in Lua.</summary>
    public static class MResTurfCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Turf.asset";
        [MenuItem("Tools/MRes/Create or Locate Turf Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "TurfAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Turf authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/TurfShaderRegistry.asset");
            var soil = Material(shader, "TurfSoil", new Color(.26f, .13f, .055f, 1));
            var green = Material(shader, "TurfGreen", new Color(.11f, .48f, .09f, 1));
            var lime = Material(shader, "TurfLime", new Color(.42f, .72f, .12f, 1));
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "TurfCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/TurfCylinder.asset");
            var root = new GameObject("Turf"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var baseVisual = Visual(root, "Soil", cylinder, soil, new Vector3(0, .06f, 0), new Vector3(.5f, .06f, .5f));
                var collider = baseVisual.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "GrassCrown", cylinder, green, new Vector3(0, .125f, 0), new Vector3(.49f, .018f, .49f));
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.PI * 2 / 5;
                    var blade = Visual(root, "Blade" + i, cylinder, i % 2 == 0 ? lime : green,
                        new Vector3(Mathf.Cos(a) * .13f, .17f, Mathf.Sin(a) * .13f), new Vector3(.035f, .055f, .06f));
                    blade.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 20, i * 72, Mathf.Cos(a) * 20);
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/Turf.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/Turf.lua");
            profile.Metadata = new MResMetadataData { name = "草皮", description = "首次碰撞后5秒内有另一颗弹珠碰撞，生成价值与累计装置倍率分别为两球平均值的新球，向下以速度6射出。原球正常反弹，成功生成后重置并扣1耐久；同球及自身后代不参与配对。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Turf.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/Turf.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "TurfAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Material Material(Shader shader, string name, Color color)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", 0); result.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_TURF_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
