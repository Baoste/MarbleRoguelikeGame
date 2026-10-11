using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Authoring only; the device effect lives entirely in Collector.lua.</summary>
    public static class MResCollectorCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Collector.asset";

        [MenuItem("Tools/MRes/Create or Locate Collector Device")]
        public static void Locate()
        {
            Selection.activeObject = Create();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "CollectorAuthoring"); return existing; }
            const string rootPath = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" })
                Directory.CreateDirectory(rootPath + "/" + sub);
            AssetDatabase.Refresh();
            // Use the same explicit shader ID as the receiving game's registry. URP is an authoring dependency.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Collector authoring requires the Universal Render Pipeline/Lit shader.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, rootPath + "/Profiles/CollectorShaderRegistry.asset");
            var material = new Material(shader) { name = "CollectorViolet" };
            material.SetColor("_BaseColor", new Color(.6f, .3f, .95f, 1));
            material.SetFloat("_Smoothness", .8f);
            AssetDatabase.CreateAsset(material, rootPath + "/Materials/CollectorViolet.mat");

            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "CollectorCylinder";
            AssetDatabase.CreateAsset(mesh, rootPath + "/Models/CollectorCylinder.asset");
            var root = new GameObject("Collector");
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
                prefab = PrefabUtility.SaveAsPrefabAsset(root, rootPath + "/Prefabs/Collector.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab;
            profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(rootPath + "/Lua/Collector.lua");
            profile.Metadata = new MResMetadataData { name = "吸吸", description = "吸引附近弹珠聚成一团，2 秒后保留各自进入时的速度大小，沿棋盘向下释放。", value = 120, type = "device" };
            profile.ShaderRegistry = registry;
            profile.OutputPath = "Assets/MResPacker/Output/Collector.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(rootPath + "/Data/Collector.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath);
            MResExampleCreator.EnsureScene(prefab, "CollectorAuthoring");
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static void CreateAndBuild()
        {
            Debug.Log("MRES_COLLECTOR_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
        }
    }
}
