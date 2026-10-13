using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    public static class MResSplitterCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Splitter.asset";
        [MenuItem("Tools/MRes/Create or Locate Splitter Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "SplitterAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Splitter authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/SplitterShaderRegistry.asset");
            var blue = Material(shader, "SplitterBlue", new Color(.12f, .43f, .62f, 1), .55f);
            var gold = Material(shader, "SplitterGold", new Color(1, .68f, .16f, 1), .7f);
            var dark = Material(shader, "SplitterDark", new Color(.025f, .045f, .06f, 1), .2f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "SplitterCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/SplitterCylinder.asset");
            var root = new GameObject("Splitter"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Hub", cylinder, blue, new Vector3(0, .13f, .06f), new Vector3(.46f, .13f, .46f));
                Visual(root, "Cap", cylinder, gold, new Vector3(0, .27f, .06f), new Vector3(.3f, .015f, .3f));
                for (int i = -1; i <= 1; i++)
                {
                    var direction = Quaternion.AngleAxis(i * 30, Vector3.up) * Vector3.back;
                    var rotation = Quaternion.FromToRotation(Vector3.up, direction);
                    var center = Vector3.up * .12f;
                    var pipe = Visual(root, "Outlet" + (i + 1), cylinder, blue, center + direction * .29f, new Vector3(.16f, .22f, .16f));
                    pipe.transform.localRotation = rotation;
                    var rim = Visual(root, "Rim" + (i + 1), cylinder, gold, center + direction * .51f, new Vector3(.19f, .025f, .19f));
                    rim.transform.localRotation = rotation;
                    var opening = Visual(root, "Opening" + (i + 1), cylinder, dark, center + direction * .537f, new Vector3(.13f, .004f, .13f));
                    opening.transform.localRotation = rotation;
                }
                var trigger = root.AddComponent<BoxCollider>(); trigger.isTrigger = true;
                trigger.center = new Vector3(0, .15f, .06f); trigger.size = new Vector3(.46f, .3f, .46f);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/Splitter.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/Splitter.lua");
            profile.Metadata = new MResMetadataData { name = "分流器", description = "弹珠进入后均分为3颗小球，每颗价值和体积各为原球1/3，继承累计装置倍率，保持入口速度大小从向下及左右30°三口射出。成功分流扣1耐久，分出的球再次经过同一台时直接穿过。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Splitter.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/Splitter.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "SplitterAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Material Material(Shader shader, string name, Color color, float metallic)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", .5f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false); result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_SPLITTER_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
