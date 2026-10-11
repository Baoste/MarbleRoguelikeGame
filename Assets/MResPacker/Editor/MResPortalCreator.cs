using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable authoring source for the game's existing paired portal package (ID 103).</summary>
    public static class MResPortalCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Portal.asset";
        [MenuItem("Tools/MRes/Create or Locate Portal Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "PortalAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Portal authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/PortalShaderRegistry.asset");
            var surface = new Material(shader) { name = "PortalIndigo" };
            surface.SetColor("_BaseColor", new Color(.16f, .12f, .48f, 1)); surface.SetFloat("_Smoothness", .8f);
            AssetDatabase.CreateAsset(surface, folder + "/Materials/PortalIndigo.mat");
            var glow = new Material(shader) { name = "PortalRim" };
            glow.SetColor("_BaseColor", new Color(.5f, .45f, 1, 1)); glow.SetColor("_EmissionColor", new Color(.5f, .35f, 1));
            glow.EnableKeyword("_EMISSION"); AssetDatabase.CreateAsset(glow, folder + "/Materials/PortalRim.mat");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "PortalCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/PortalCylinder.asset");
            var rim = Ring(); AssetDatabase.CreateAsset(rim, folder + "/Models/PortalRim.asset");
            var root = new GameObject("Portal"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var visual = new GameObject("Cylinder"); visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0, .04f, 0); visual.transform.localScale = new Vector3(.24f, .04f, .24f);
                visual.AddComponent<MeshFilter>().sharedMesh = cylinder; visual.AddComponent<MeshRenderer>().sharedMaterial = surface;
                var sensor = visual.AddComponent<MeshCollider>(); sensor.sharedMesh = cylinder; sensor.convex = true; sensor.isTrigger = true;
                var ring = new GameObject("Rim"); ring.transform.SetParent(root.transform, false); ring.transform.localPosition = Vector3.up * .081f;
                ring.AddComponent<MeshFilter>().sharedMesh = rim; ring.AddComponent<MeshRenderer>().sharedMaterial = glow;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/Portal.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/Portal.lua");
            profile.Metadata = new MResMetadataData { name = "传送门", description = "购买一对门，双向传送；保持世界运动方向、速度和自身重力等属性，区域力按出口环境计算。", value = 15, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Portal.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/Portal.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "PortalAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }

        private static Mesh Ring()
        {
            const int segments = 48;
            var vertices = new Vector3[segments * 2]; var uv = new Vector2[vertices.Length]; var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                var heading = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 2] = heading * .095f; vertices[i * 2 + 1] = heading * .118f;
                uv[i * 2] = new Vector2((float)i / segments, 0); uv[i * 2 + 1] = new Vector2((float)i / segments, 1);
                int next = ((i + 1) % segments) * 2, index = i * 6;
                triangles[index] = i * 2; triangles[index + 1] = next + 1; triangles[index + 2] = i * 2 + 1;
                triangles[index + 3] = i * 2; triangles[index + 4] = next; triangles[index + 5] = next + 1;
            }
            var mesh = new Mesh { name = "PortalRim", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_PORTAL_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
