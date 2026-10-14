using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Five-point lamp with two visual faces and one non-solid entry region.</summary>
    public static class MResStarLampCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/StarLamp.asset";
        [MenuItem("Tools/MRes/Create or Locate Star Lamp Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "StarLampAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Star lamp authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/StarLampShaderRegistry.asset");
            var shell = Material(shader, "StarLampFrame", new Color(.09f, .18f, .23f, 1), false);
            var unlit = Material(shader, "StarLampUnlit", new Color(.3f, .23f, .07f, 1), false);
            var lit = Material(shader, "StarLampLit", new Color(1, .85f, .2f, 1), true);
            var mesh = StarMesh(); AssetDatabase.CreateAsset(mesh, folder + "/Models/StarLampStar.asset");
            var root = new GameObject("StarLamp"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Frame", mesh, shell, .02f, new Vector3(.36f, .1f, .36f));
                Visual(root, "Unlit", mesh, unlit, .12f, new Vector3(.31f, .015f, .31f));
                Visual(root, "Lit", mesh, lit, .12f, new Vector3(.31f, .015f, .31f)).enabled = false;
                var trigger = root.AddComponent<BoxCollider>(); trigger.isTrigger = true;
                trigger.center = new Vector3(0, .15f, 0); trigger.size = new Vector3(.72f, .3f, .72f);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/StarLamp.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/StarLamp.lua");
            profile.Metadata = new MResMetadataData { name = "星星灯", description = "无实体碰撞，弹珠进入切换开关；场上至少2盏且全部点亮后，所有弹珠获得灯总数点临时附加倍率，持续5秒，期间保持亮灯且不重复叠加。时间结束移除加成、全部熄灭，参与的每盏灯扣1耐久。每回合初始关闭。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/StarLamp.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/StarLamp.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "StarLampAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Mesh StarMesh()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            Vector3 Point(int i, float y)
            {
                float angle = i * Mathf.PI / 5, radius = i % 2 == 0 ? 1 : .45f;
                return new Vector3(Mathf.Sin(angle) * radius, y, Mathf.Cos(angle) * radius);
            }
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                foreach (var p in new[] { a, b, c })
                { triangles.Add(vertices.Count); vertices.Add(p); uv.Add(new Vector2(p.x * .5f + .5f, p.z * .5f + .5f)); }
            }
            for (int i = 0; i < 10; i++)
            {
                int next = (i + 1) % 10;
                Triangle(Vector3.up, Point(i, 1), Point(next, 1));
                Triangle(Vector3.zero, Point(next, 0), Point(i, 0));
                Triangle(Point(i, 0), Point(next, 0), Point(next, 1));
                Triangle(Point(i, 0), Point(next, 1), Point(i, 1));
            }
            var mesh = new Mesh { name = "StarLampStar" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Material Material(Shader shader, string name, Color color, bool emission)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", emission ? .05f : .4f); result.SetFloat("_Smoothness", .55f);
            if (emission)
            {
                result.SetColor("_EmissionColor", new Color(2.5f, 1.5f, .12f, 1));
                result.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                result.EnableKeyword("_EMISSION");
            }
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static MeshRenderer Visual(GameObject root, string name, Mesh mesh, Material material, float height, Vector3 scale)
        {
            var node = new GameObject(name); node.transform.SetParent(root.transform, false);
            node.transform.localPosition = Vector3.up * height; node.transform.localScale = scale;
            node.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = node.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_STAR_LAMP_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
