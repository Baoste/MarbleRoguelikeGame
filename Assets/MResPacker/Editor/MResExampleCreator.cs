using System;
using System.IO;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialType = UnityEngine.PhysicsMaterial;
using CombineType = UnityEngine.PhysicsMaterialCombine;
#else
using PhysicsMaterialType = UnityEngine.PhysicMaterial;
using CombineType = UnityEngine.PhysicMaterialCombine;
#endif

namespace MResPacker
{
    public static class MResExampleCreator
    {
        public const string Root = "Assets/MResPacker/Authoring";
        public const string ProfilePath = Root + "/Profiles/BouncePad.asset";
        [MenuItem("Tools/MRes/Create or Locate Packer Example")]
        public static void CreateMenu() { var profile = Create(); Selection.activeObject = profile; EditorGUIUtility.PingObject(profile); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { EnsureScene(existing.SourcePrefab); return existing; } // Never replace authored changes.
            foreach (string sub in new[] { "Profiles", "Models", "Materials", "Textures", "Prefabs", "Scenes" }) Directory.CreateDirectory(Root + "/" + sub);
            Directory.CreateDirectory("Assets/MResPacker/Output"); AssetDatabase.Refresh();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/ExampleSurface.shader");
            if (shader == null) throw new InvalidOperationException("Missing standalone ExampleSurface.shader.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "MRes/ExampleSurface" } };
            AssetDatabase.CreateAsset(registry, Root + "/Profiles/ShaderRegistry.asset");

            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, true, false);
            try
            {
                var colors = new Color32[256];
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) colors[y * 16 + x] = (x / 4 + y / 4) % 2 == 0 ? new Color32(240, 190, 70, 255) : new Color32(65, 105, 150, 255);
                texture.SetPixels32(colors); texture.Apply(); File.WriteAllBytes(Root + "/Textures/PadChecker.png", texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(Root + "/Textures/PadChecker.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/PadChecker.png");
            importer.isReadable = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.filterMode = FilterMode.Point; importer.SaveAndReimport();
            var image = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/PadChecker.png");
            var first = new Material(shader) { name = "Pad Gold", enableInstancing = true };
            first.SetTexture("_BaseMap", image); first.SetColor("_BaseColor", Color.white); first.SetInteger("_Mode", 2);
            first.SetVector("_Offset", new Vector4(.1f, .2f, .3f, .4f)); first.SetFloat("_Gain", 1.25f);
            var second = new Material(first) { name = "Pad Blue" }; second.SetColor("_BaseColor", new Color(.35f, .65f, 1, 1));
            AssetDatabase.CreateAsset(first, Root + "/Materials/PadGold.mat"); AssetDatabase.CreateAsset(second, Root + "/Materials/PadBlue.mat");
            var physics = new PhysicsMaterialType("PadBounce") { dynamicFriction = .25f, staticFriction = .35f, bounciness = .7f,
                frictionCombine = CombineType.Multiply, bounceCombine = CombineType.Maximum };
            AssetDatabase.CreateAsset(physics, Root + "/Materials/PadBounce.physicMaterial");

            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "TwoMaterialCube"; var indices = mesh.triangles;
            var a = new int[6]; var b = new int[indices.Length - 6]; Array.Copy(indices, a, a.Length); Array.Copy(indices, 6, b, 0, b.Length);
            mesh.subMeshCount = 2; mesh.SetTriangles(a, 0); mesh.SetTriangles(b, 1); mesh.uv2 = mesh.uv;
            var vertexColors = new Color[mesh.vertexCount]; for (int i = 0; i < vertexColors.Length; i++) vertexColors[i] = Color.white; mesh.colors = vertexColors;
            AssetDatabase.CreateAsset(mesh, Root + "/Models/TwoMaterialCube.asset");
            var root = new GameObject("BouncePad"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false; body.mass = 2;
                body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var box = root.AddComponent<BoxCollider>(); box.center = new Vector3(0, .04f, 0); box.size = new Vector3(.42f, .08f, .42f); box.sharedMaterial = physics;
                var group = new GameObject("Visual"); group.transform.SetParent(root.transform, false);
                var model = new GameObject("Base"); model.transform.SetParent(group.transform, false);
                model.transform.localPosition = new Vector3(0, .04f, 0); model.transform.localScale = new Vector3(.42f, .08f, .42f);
                model.AddComponent<MeshFilter>().sharedMesh = mesh; model.AddComponent<MeshRenderer>().sharedMaterials = new[] { first, second };
                var arrow = new GameObject("ExitMarker"); arrow.transform.SetParent(group.transform, false);
                arrow.transform.localPosition = new Vector3(0, .09f, -.12f); arrow.transform.localScale = new Vector3(.08f, .02f, .15f);
                arrow.AddComponent<MeshFilter>().sharedMesh = mesh; arrow.AddComponent<MeshRenderer>().sharedMaterials = new[] { second, first };
                prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/BouncePad.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(Root + "/Lua/BouncePad.lua"); profile.ShaderRegistry = registry;
            profile.Metadata = new MResMetadataData { name = "示例弹射台", description = "保持或提高速度，并沿装置出口朝向弹出。", value = 120, type = "device" };
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/BouncePad.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath);
            EnsureScene(prefab);
            AssetDatabase.SaveAssets(); return profile;
        }
        internal static void EnsureScene(GameObject prefab, string sceneName = "MResAuthoring")
        {
            if (File.Exists(Root + "/Scenes/" + sceneName + ".unity")) return;
            Directory.CreateDirectory(Root + "/Scenes");
            // Author two ordinary prefab instances without opening/replacing any user scene.
            // Unity refuses to add a regular scene beside an unsaved untitled scene.
            var cameraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/AuthoringCamera.prefab");
            if (cameraPrefab == null)
            {
                var cameraObject = new GameObject("Authoring Camera");
                try
                {
                    cameraObject.transform.position = new Vector3(.65f, .85f, -.9f); cameraObject.transform.LookAt(new Vector3(0, .05f, 0));
                    var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.055f, .065f, .085f); camera.nearClipPlane = .01f;
                    cameraPrefab = PrefabUtility.SaveAsPrefabAsset(cameraObject, Root + "/Prefabs/AuthoringCamera.prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(cameraObject); }
            }
            var yaml = new System.Text.StringBuilder("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            SceneInstance(yaml, prefab, 1000); SceneInstance(yaml, cameraPrefab, 2000);
            yaml.Append("--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots:\n  - {fileID: 1001}\n  - {fileID: 2001}\n");
            string path = Root + "/Scenes/" + sceneName + ".unity";
            File.WriteAllText(path, yaml.ToString(), MResJson.Utf8); AssetDatabase.ImportAsset(path);
        }
        private static void SceneInstance(System.Text.StringBuilder yaml, GameObject prefab, int id)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab.transform, out string guid, out long localId))
                throw new IOException("Cannot resolve authoring prefab.");
            yaml.Append($"--- !u!1001 &{id}\nPrefabInstance:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Modification:\n    serializedVersion: 3\n    m_TransformParent: {{fileID: 0}}\n    m_Modifications: []\n    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n    m_AddedComponents: []\n  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}\n");
            yaml.Append($"--- !u!4 &{id + 1} stripped\nTransform:\n  m_CorrespondingSourceObject: {{fileID: {localId}, guid: {guid}, type: 3}}\n  m_PrefabInstance: {{fileID: {id}}}\n  m_PrefabAsset: {{fileID: 0}}\n");
        }
        // Batch-mode entry point used to create the same checked-in sample assets.
        public static void CreateAndBuild()
        {
            var profile = Create(); var report = MResBuildPipeline.Build(profile);
            Debug.Log("MRES_PACKER_EXAMPLE_PASSED: " + report);
        }
    }
}
