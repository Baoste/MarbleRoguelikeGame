using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Authoring assets only. Copy and reflection decisions live in Duplicator.lua.</summary>
    public static class MResDuplicatorCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Duplicator.asset";
        [MenuItem("Tools/MRes/Create or Locate Duplicator Device")]
        public static void Locate()
        {
            Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject);
        }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "DuplicatorAuthoring"); return existing; }
            const string rootPath = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(rootPath + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Duplicator authoring requires Universal Render Pipeline/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, rootPath + "/Profiles/DuplicatorShaderRegistry.asset");
            var material = new Material(shader) { name = "DuplicatorSilver" };
            material.SetColor("_BaseColor", new Color(.55f, .8f, 1, 1)); material.SetFloat("_Smoothness", .65f);
            AssetDatabase.CreateAsset(material, rootPath + "/Materials/DuplicatorSilver.mat");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "DuplicatorCylinder"; AssetDatabase.CreateAsset(mesh, rootPath + "/Models/DuplicatorCylinder.asset");
            var root = new GameObject("Duplicator"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var visual = new GameObject("Cylinder"); visual.transform.SetParent(root.transform, false);
                // Tall enough to hit the cylindrical side rather than only its top rim.
                visual.transform.localPosition = new Vector3(0, .1f, 0);
                visual.transform.localScale = new Vector3(.4f, .1f, .4f);
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = material;
                var collider = visual.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = true;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, rootPath + "/Prefabs/Duplicator.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(rootPath + "/Lua/Duplicator.lua");
            profile.Metadata = new MResMetadataData { name = "复制体", description = "碰撞后生成一颗初始价值为0的复制球，尺寸和质量不变，沿入射速度的镜面反射方向等速射出；原球正常反弹。复制球可触发其他装置。每颗原球仅复制一次，自己的复制后代不再触发，成功复制扣1耐久。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Duplicator.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(rootPath + "/Data/Duplicator.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "DuplicatorAuthoring");
            AssetDatabase.SaveAssets(); return profile;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_DUPLICATOR_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
