using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Authoring only; the device effect lives entirely in AntiGravity.lua.</summary>
    public static class MResAntiGravityCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/AntiGravity.asset";

        [MenuItem("Tools/MRes/Create or Locate AntiGravity Device")]
        public static void Locate()
        {
            Selection.activeObject = Create();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "AntiGravityAuthoring"); return existing; }
            const string rootPath = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" })
                Directory.CreateDirectory(rootPath + "/" + sub);
            AssetDatabase.Refresh();
            // Use the same explicit shader ID as the receiving game's registry. URP is an authoring dependency.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("AntiGravity authoring requires the Universal Render Pipeline/Lit shader.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, rootPath + "/Profiles/AntiGravityShaderRegistry.asset");
            var material = new Material(shader) { name = "AntiGravityGold" };
            material.SetColor("_BaseColor", new Color(1f, .7f, .12f, 1));
            material.SetFloat("_Smoothness", .8f);
            AssetDatabase.CreateAsset(material, rootPath + "/Materials/AntiGravityGold.mat");

            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "AntiGravityCylinder";
            AssetDatabase.CreateAsset(mesh, rootPath + "/Models/AntiGravityCylinder.asset");
            var root = new GameObject("AntiGravity");
            GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var visual = new GameObject("Cylinder"); visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0, .05f, 0);
                visual.transform.localScale = new Vector3(.4f, .05f, .4f);
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = material;
                var sensor = visual.AddComponent<MeshCollider>(); sensor.sharedMesh = mesh; sensor.convex = true; sensor.isTrigger = true;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, rootPath + "/Prefabs/AntiGravity.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab;
            profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(rootPath + "/Lua/AntiGravity.lua");
            profile.Metadata = new MResMetadataData { name = "反重力", description = "弹珠从装置上方朝上射出，保留速度大小，反转重力 2 秒；撞击其他装置时提前恢复。", value = 120, type = "device" };
            profile.ShaderRegistry = registry;
            profile.OutputPath = "Assets/MResPacker/Output/AntiGravity.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(rootPath + "/Data/AntiGravity.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath);
            MResExampleCreator.EnsureScene(prefab, "AntiGravityAuthoring");
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static void CreateAndBuild()
        {
            Debug.Log("MRES_ANTI_GRAVITY_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
        }
    }
}
