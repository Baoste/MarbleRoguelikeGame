using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable oversized iced coffee cup; all effects live in Lua.</summary>
    public static class MResIcedAmericanoCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/IcedAmericano.asset";
        public const string Description = "弹珠碰撞后，额外增加20点RUSH积累贡献，当前基础价值减半（保留小数）。每次有效碰撞扣1耐久，可重复触发；通用触发的5点贡献照常计入，到底部或得分区才向RUSH条入账。";

        [MenuItem("Tools/MRes/Create or Locate Iced Americano Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "IcedAmericanoAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Iced coffee authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/IcedAmericanoShaderRegistry.asset");
            var cup = Material(shader, "IcedAmericanoCup", new Color(.91f, .84f, .66f, 1));
            var coffee = Material(shader, "IcedAmericanoCoffee", new Color(.17f, .065f, .025f, 1));
            var ice = Material(shader, "IcedAmericanoIce", new Color(.63f, .89f, .98f, 1));
            var green = Material(shader, "IcedAmericanoGreen", new Color(.075f, .36f, .25f, 1));
            var cylinder = PrimitiveMesh(PrimitiveType.Cylinder); var cube = PrimitiveMesh(PrimitiveType.Cube);
            var root = new GameObject("IcedAmericano"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Cup", cylinder, cup, new Vector3(0, .27f, 0), new Vector3(.56f, .27f, .56f));
                Visual(root, "Sleeve", cylinder, green, new Vector3(0, .25f, 0), new Vector3(.57f, .09f, .57f));
                Visual(root, "Rim", cylinder, cup, new Vector3(0, .55f, 0), new Vector3(.63f, .025f, .63f));
                Visual(root, "Coffee", cylinder, coffee, new Vector3(0, .579f, 0), new Vector3(.55f, .006f, .55f));
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.PI / 3;
                    var node = Visual(root, "Ice" + (i + 1), cube, ice,
                        new Vector3(Mathf.Cos(angle) * .155f, .61f, Mathf.Sin(angle) * .155f), new Vector3(.13f, .09f, .13f));
                    node.transform.localRotation = Quaternion.Euler(5, i * 37, 8);
                }
                var straw = Visual(root, "Straw", cylinder, green, new Vector3(.07f, .74f, .025f), new Vector3(.035f, .2f, .035f));
                straw.transform.localRotation = Quaternion.Euler(0, 0, -12);
                var collider = root.AddComponent<CapsuleCollider>();
                collider.center = new Vector3(0, .285f, 0); collider.radius = .285f; collider.height = .57f;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/IcedAmericano.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/IcedAmericano.lua");
            profile.Metadata = new MResMetadataData { name = "超大杯全冰美式", description = Description, value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/IcedAmericano.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/IcedAmericano.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "IcedAmericanoAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var primitive = GameObject.CreatePrimitive(type); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "IcedAmericano" + type; AssetDatabase.CreateAsset(mesh, MResExampleCreator.Root + "/Models/" + mesh.name + ".asset"); return mesh;
        }
        private static Material Material(Shader shader, string name, Color color)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", .7f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var node = new GameObject(name); node.transform.SetParent(root.transform, false); node.transform.localPosition = position; node.transform.localScale = scale;
            node.AddComponent<MeshFilter>().sharedMesh = mesh; node.AddComponent<MeshRenderer>().sharedMaterial = material; return node;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_ICED_AMERICANO_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
