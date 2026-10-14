using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable open wok, rim and handles; all cooking rules are Lua.</summary>
    public static class MResWokCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Wok.asset";
        public const string Description = "弹珠入锅后RUSH积累贡献额外+10，连同装置有效触发的+5共增加15。无碰撞翻炒1秒后保留进入速度，向棋盘上方±60°随机抛出，成功抛出扣1耐久。最后一球离锅后冷却5秒，冷却期间直接穿过。贡献在落入底部或得分区时才累加到全盘RUSH条。";
        [MenuItem("Tools/MRes/Create or Locate Wok Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { existing.Metadata.description = Description; EditorUtility.SetDirty(existing); MResExampleCreator.EnsureScene(existing.SourcePrefab, "WokAuthoring"); AssetDatabase.SaveAssets(); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Wok authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/WokShaderRegistry.asset");
            var iron = Material(shader, "WokIron", new Color(.065f, .075f, .09f, 1), .8f);
            var rim = Material(shader, "WokCopper", new Color(.66f, .36f, .13f, 1), .7f);
            var wood = Material(shader, "WokHandle", new Color(.35f, .14f, .055f, 1), .05f);
            var bowl = Lathe("WokBowl", new[] { new Vector2(0, .015f), new Vector2(.13f, .015f), new Vector2(.24f, .07f),
                new Vector2(.34f, .19f), new Vector2(.32f, .19f), new Vector2(.22f, .09f), new Vector2(.12f, .04f), new Vector2(0, .04f) });
            var lip = Lathe("WokRim", new[] { new Vector2(.34f, .18f), new Vector2(.35f, .19f), new Vector2(.34f, .2f),
                new Vector2(.32f, .2f), new Vector2(.31f, .19f), new Vector2(.32f, .18f), new Vector2(.34f, .18f) });
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); Mesh cube;
            try { cube = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            cube.name = "WokCube"; AssetDatabase.CreateAsset(cube, folder + "/Models/WokCube.asset");
            var root = new GameObject("Wok"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Bowl", bowl, iron, Vector3.zero, Vector3.one);
                Visual(root, "Rim", lip, rim, Vector3.zero, Vector3.one);
                foreach (int side in new[] { -1, 1 })
                {
                    Visual(root, "HandleArm" + side, cube, iron, new Vector3(side * .37f, .17f, 0), new Vector3(.12f, .04f, .07f));
                    Visual(root, "Grip" + side, cube, wood, new Vector3(side * .43f, .17f, 0), new Vector3(.065f, .07f, .21f));
                }
                var sensor = root.AddComponent<SphereCollider>(); sensor.isTrigger = true;
                sensor.center = new Vector3(0, .13f, 0); sensor.radius = .33f;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/Wok.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/Wok.lua");
            profile.Metadata = new MResMetadataData { name = "有锅气", description = Description, value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/Wok.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/Wok.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "WokAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Mesh Lathe(string name, Vector2[] profile)
        {
            const int segments = 40; var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (int row = 0; row < profile.Length; row++)
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * profile[row].x, profile[row].y, Mathf.Sin(angle) * profile[row].x));
                    uv.Add(new Vector2((float)i / segments, (float)row / (profile.Length - 1)));
                }
            for (int row = 0; row < profile.Length - 1; row++)
                for (int i = 0; i < segments; i++)
                {
                    int a = row * (segments + 1) + i, b = a + segments + 1;
                    triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, MResExampleCreator.Root + "/Models/" + name + ".asset"); return mesh;
        }
        private static Material Material(Shader shader, string name, Color color, float metallic)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", .45f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static void Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var node = new GameObject(name); node.transform.SetParent(root.transform, false); node.transform.localPosition = position; node.transform.localScale = scale;
            node.AddComponent<MeshFilter>().sharedMesh = mesh; node.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_WOK_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
