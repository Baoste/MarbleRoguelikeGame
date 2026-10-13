using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    public static class MResBombCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Bomb.asset";
        [MenuItem("Tools/MRes/Create or Locate Bomb Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "BombAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Bomb authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/BombShaderRegistry.asset");
            var iron = Material(shader, "BombIron", new Color(.075f, .09f, .12f, 1));
            var brass = Material(shader, "BombBrass", new Color(.65f, .42f, .13f, 1));
            var fire = Material(shader, "BombFire", new Color(1, .38f, .04f, 1));
            var sphere = Mesh(PrimitiveType.Sphere, "BombSphere");
            var cylinder = Mesh(PrimitiveType.Cylinder, "BombCylinder");
            var root = new GameObject("Bomb"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Base", cylinder, brass, new Vector3(0, .045f, 0), new Vector3(.5f, .045f, .5f));
                Visual(root, "Launcher", sphere, iron, new Vector3(0, .17f, 0), new Vector3(.38f, .28f, .38f));
                Visual(root, "Rim", cylinder, brass, new Vector3(0, .305f, 0), new Vector3(.23f, .035f, .23f));
                Visual(root, "Mouth", cylinder, iron, new Vector3(0, .343f, 0), new Vector3(.16f, .004f, .16f));
                var template = new GameObject("BombTemplate"); template.transform.SetParent(root.transform, false);
                Visual(template, "Shell", sphere, iron, Vector3.zero, Vector3.one * .15f);
                var fuse = Visual(template, "Fuse", cylinder, brass, new Vector3(.025f, .085f, 0), new Vector3(.018f, .03f, .018f));
                fuse.transform.localRotation = Quaternion.Euler(0, 0, -25);
                Visual(template, "Spark", sphere, fire, new Vector3(.04f, .11f, 0), Vector3.one * .035f);
                template.SetActive(false);
                var flash = new GameObject("BlastTemplate"); flash.transform.SetParent(root.transform, false);
                Visual(flash, "Burst", sphere, fire, Vector3.zero, new Vector3(1.6f, .025f, 1.6f));
                flash.SetActive(false);
                var collider = root.AddComponent<BoxCollider>(); collider.center = new Vector3(0, .15f, 0); collider.size = new Vector3(.34f, .3f, .34f);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/Bomb.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/Bomb.lua");
            profile.Metadata = new MResMetadataData { name = "炸弹", description = "每5秒沿随机方向投出炸弹，速度3，1秒后爆炸；半径0.8内每颗弹珠均分8球，价值和体积均分，继承累计倍率，以速度6向四周散开。每次爆炸扣1耐久，空爆也扣；后代不再被同一台分裂。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Bomb.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/Bomb.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "BombAuthoring"); AssetDatabase.SaveAssets(); return profile;
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
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Smoothness", .35f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false); result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_BOMB_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
