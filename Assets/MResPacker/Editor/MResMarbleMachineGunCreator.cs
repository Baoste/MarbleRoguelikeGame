using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    public static class MResMarbleMachineGunCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/MarbleMachineGun.asset";
        [MenuItem("Tools/MRes/Create or Locate Marble Machine Gun Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "MarbleMachineGunAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Marble machine gun authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/MarbleMachineGunShaderRegistry.asset");
            var metal = Material(shader, "MarbleMachineGunSteel", new Color(.18f, .29f, .34f, 1), .7f);
            var brass = Material(shader, "MarbleMachineGunBrass", new Color(.9f, .63f, .19f, 1), .65f);
            var dark = Material(shader, "MarbleMachineGunDark", new Color(.02f, .03f, .04f, 1), .25f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "MarbleMachineGunCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/MarbleMachineGunCylinder.asset");
            var root = new GameObject("MarbleMachineGun"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "AmmoDrum", cylinder, metal, new Vector3(0, .1f, 0), new Vector3(.4f, .1f, .4f));
                Visual(root, "IntakeRim", cylinder, brass, new Vector3(0, .21f, 0), new Vector3(.3f, .02f, .3f));
                Visual(root, "Intake", cylinder, dark, new Vector3(0, .235f, 0), new Vector3(.23f, .004f, .23f));
                var rotation = Quaternion.FromToRotation(Vector3.up, Vector3.back);
                var barrel = Visual(root, "Barrel", cylinder, metal, new Vector3(0, .06f, -.27f), new Vector3(.1f, .16f, .1f)); barrel.transform.localRotation = rotation;
                var rim = Visual(root, "MuzzleRim", cylinder, brass, new Vector3(0, .06f, -.43f), new Vector3(.13f, .018f, .13f)); rim.transform.localRotation = rotation;
                var mouth = Visual(root, "Muzzle", cylinder, dark, new Vector3(0, .06f, -.452f), new Vector3(.08f, .004f, .08f)); mouth.transform.localRotation = rotation;
                var trigger = root.AddComponent<SphereCollider>(); trigger.isTrigger = true; trigger.center = new Vector3(0, .12f, 0); trigger.radius = .2f;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/MarbleMachineGun.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/MarbleMachineGun.lua");
            profile.Metadata = new MResMetadataData { name = "弹珠机关枪", description = "将弹珠基础价值拆成每颗价值1的小球，舍弃不足1的余数，继承累计倍率。每0.1秒以速度6从下方45°扇形往返扫射，整批完成扣1耐久；忙碌时其他球及自身后代直接穿过。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/MarbleMachineGun.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/MarbleMachineGun.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "MarbleMachineGunAuthoring"); AssetDatabase.SaveAssets(); return profile;
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
        public static void CreateAndBuild() => Debug.Log("MRES_MARBLE_MACHINE_GUN_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
