using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Authoring assets only. Reflection and random-angle decisions live in Reflector.lua.</summary>
    public static class MResReflectorCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Reflector.asset";
        [MenuItem("Tools/MRes/Create or Locate Reflector Device")]
        public static void Locate()
        {
            Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject);
        }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "ReflectorAuthoring"); return existing; }
            const string rootPath = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(rootPath + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Reflector authoring requires Universal Render Pipeline/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, rootPath + "/Profiles/ReflectorShaderRegistry.asset");
            var material = new Material(shader) { name = "ReflectorCoral" };
            material.SetColor("_BaseColor", new Color(1, .35f, .16f, 1)); material.SetFloat("_Smoothness", .65f);
            AssetDatabase.CreateAsset(material, rootPath + "/Materials/ReflectorCoral.mat");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "ReflectorCylinder"; AssetDatabase.CreateAsset(mesh, rootPath + "/Models/ReflectorCylinder.asset");
            var root = new GameObject("Reflector"); GameObject prefab;
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
                prefab = PrefabUtility.SaveAsPrefabAsset(root, rootPath + "/Prefabs/Reflector.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(rootPath + "/Lua/Reflector.lua");
            profile.Metadata = new MResMetadataData { name = "弹弹", description = "保留入射速度大小，在镜面反射方向的 ±30° 范围内随机弹出。", value = 120, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Reflector.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(rootPath + "/Data/Reflector.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "ReflectorAuthoring");
            AssetDatabase.SaveAssets(); return profile;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_REFLECTOR_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
