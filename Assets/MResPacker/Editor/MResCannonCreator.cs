using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    public static class MResCannonCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Cannon.asset";
        [MenuItem("Tools/MRes/Create or Locate Cannon Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "CannonAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Cannon authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/CannonShaderRegistry.asset");
            var metal = Material(shader, "CannonBronze", new Color(.55f, .3f, .08f, 1));
            var dark = Material(shader, "CannonBore", new Color(.035f, .04f, .05f, 1));
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "CannonCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/CannonCylinder.asset");
            var root = new GameObject("Cannon"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var baseNode = Visual(root, "Base", cylinder, metal, new Vector3(0, .1f, 0), new Vector3(.5f, .1f, .5f), Quaternion.identity);
                var sensor = baseNode.AddComponent<MeshCollider>(); sensor.sharedMesh = cylinder; sensor.convex = true; sensor.isTrigger = true;
                // Local -Z is the engine's Forward. The dark muzzle makes all eight headings readable.
                Visual(root, "Barrel", cylinder, metal, new Vector3(0, .16f, -.105f), new Vector3(.15f, .12f, .15f), Quaternion.Euler(90, 0, 0));
                Visual(root, "Muzzle", cylinder, dark, new Vector3(0, .16f, -.228f), new Vector3(.11f, .003f, .11f), Quaternion.Euler(90, 0, 0));
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/Cannon.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/Cannon.lua");
            profile.Metadata = new MResMetadataData { name = "大炮", description = "每0.5秒旋转45°，最多存入5颗弹珠；点击后沿当前方向以速度6全数发射。", value = 120, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Cannon.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/Cannon.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "CannonAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Material Material(Shader shader, string name, Color color)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Smoothness", .55f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale; result.transform.localRotation = rotation;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_CANNON_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
