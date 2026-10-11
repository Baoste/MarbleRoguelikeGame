using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable authoring source for the chargeable radial attraction device (ID 9005).</summary>
    public static class MResBlackHoleCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/BlackHole.asset";
        [MenuItem("Tools/MRes/Create or Locate BlackHole Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "BlackHoleAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("BlackHole authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/BlackHoleShaderRegistry.asset");
            var surface = new Material(shader) { name = "BlackHoleCore" };
            surface.SetColor("_BaseColor", new Color(.025f, .015f, .045f, 1)); surface.SetFloat("_Smoothness", .8f);
            AssetDatabase.CreateAsset(surface, folder + "/Materials/BlackHoleCore.mat");
            var glow = new Material(shader) { name = "BlackHoleRim" };
            glow.SetColor("_BaseColor", new Color(.65f, .25f, 1, 1)); glow.SetColor("_EmissionColor", new Color(.55f, .08f, 1));
            glow.EnableKeyword("_EMISSION"); AssetDatabase.CreateAsset(glow, folder + "/Materials/BlackHoleRim.mat");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Mesh cylinder;
            try { cylinder = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cylinder.name = "BlackHoleCylinder"; AssetDatabase.CreateAsset(cylinder, folder + "/Models/BlackHoleCylinder.asset");
            var rim = Ring(); AssetDatabase.CreateAsset(rim, folder + "/Models/BlackHoleRim.asset");
            var root = new GameObject("BlackHole"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var visual = new GameObject("Cylinder"); visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0, .04f, 0); visual.transform.localScale = new Vector3(.4f, .04f, .4f);
                visual.AddComponent<MeshFilter>().sharedMesh = cylinder; visual.AddComponent<MeshRenderer>().sharedMaterial = surface;
                var sensor = visual.AddComponent<MeshCollider>(); sensor.sharedMesh = cylinder; sensor.convex = true; sensor.isTrigger = true;
                var ring = new GameObject("Rim"); ring.transform.SetParent(root.transform, false); ring.transform.localPosition = Vector3.up * .081f;
                ring.AddComponent<MeshFilter>().sharedMesh = rim; ring.AddComponent<MeshRenderer>().sharedMaterial = glow;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/BlackHole.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/BlackHole.lua");
            profile.Metadata = new MResMetadataData { name = "黑洞", description = "吞噬10颗弹珠充能，点击释放10秒全局衰减引力；期间吞入的弹珠按当前价值×10直接结算。", value = 120, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/BlackHole.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/BlackHole.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "BlackHoleAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }

        private static Mesh Ring()
        {
            const int segments = 48;
            var vertices = new Vector3[segments * 2]; var uv = new Vector2[vertices.Length]; var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                var heading = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 2] = heading * .165f; vertices[i * 2 + 1] = heading * .197f;
                uv[i * 2] = new Vector2((float)i / segments, 0); uv[i * 2 + 1] = new Vector2((float)i / segments, 1);
                int next = ((i + 1) % segments) * 2, index = i * 6;
                triangles[index] = i * 2; triangles[index + 1] = next + 1; triangles[index + 2] = i * 2 + 1;
                triangles[index + 3] = i * 2; triangles[index + 4] = next; triangles[index + 5] = next + 1;
            }
            var mesh = new Mesh { name = "BlackHoleRim", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_BLACK_HOLE_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
