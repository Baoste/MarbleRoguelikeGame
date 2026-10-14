using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable celadon teapot; gameplay is authored in CoolingTea.lua.</summary>
    public static class MResCoolingTeaCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/CoolingTea.asset";
        public const string Description = "弹珠碰撞凉茶壶后，当前摩擦力×1.2、弹性×0.9，并获得触发RUSH时额外5秒的加时。同一颗球只生效一次，碰其他凉茶壶也不重复，成功作用扣1耐久。只有该球的贡献使RUSH条累满时，才使本次RUSH由10秒延长至15秒。";
        [MenuItem("Tools/MRes/Create or Locate Cooling Tea Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { existing.Metadata.description = Description; EditorUtility.SetDirty(existing); MResExampleCreator.EnsureScene(existing.SourcePrefab, "CoolingTeaAuthoring"); AssetDatabase.SaveAssets(); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Cooling tea authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/CoolingTeaShaderRegistry.asset");
            var green = Material(shader, "CoolingTeaCeladon", new Color(.28f, .64f, .49f, 1));
            var cream = Material(shader, "CoolingTeaCream", new Color(.87f, .88f, .68f, 1));
            var dark = Material(shader, "CoolingTeaMouth", new Color(.045f, .11f, .07f, 1));
            var sphere = PrimitiveMesh(PrimitiveType.Sphere); var cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            var handle = HandleMesh(); AssetDatabase.CreateAsset(handle, folder + "/Models/CoolingTeaHandle.asset");
            var root = new GameObject("CoolingTea"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Pot", sphere, green, new Vector3(0, .2f, 0), new Vector3(.5f, .42f, .46f));
                Visual(root, "Foot", cylinder, cream, new Vector3(0, .025f, 0), new Vector3(.3f, .025f, .3f));
                Visual(root, "Lid", cylinder, cream, new Vector3(0, .4f, 0), new Vector3(.26f, .025f, .26f));
                Visual(root, "Knob", sphere, green, new Vector3(0, .455f, 0), new Vector3(.085f, .07f, .085f));
                Visual(root, "Handle", handle, green, new Vector3(.21f, .2f, 0), Vector3.one);
                Vector3 axis = new Vector3(-.8f, .6f, 0), center = new Vector3(-.29f, .25f, 0);
                var spout = Visual(root, "Spout", cylinder, green, center, new Vector3(.105f, .15f, .105f));
                spout.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
                var mouth = Visual(root, "Mouth", cylinder, dark, center + axis * .151f, new Vector3(.08f, .003f, .08f));
                mouth.transform.localRotation = spout.transform.localRotation;
                var collider = root.AddComponent<SphereCollider>(); collider.center = new Vector3(0, .18f, 0); collider.radius = .24f;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/CoolingTea.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/CoolingTea.lua");
            profile.Metadata = new MResMetadataData { name = "降降火", description = Description, value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/CoolingTea.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/CoolingTea.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "CoolingTeaAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var primitive = GameObject.CreatePrimitive(type); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "CoolingTea" + type; AssetDatabase.CreateAsset(mesh, MResExampleCreator.Root + "/Models/" + mesh.name + ".asset"); return mesh;
        }
        private static Mesh HandleMesh()
        {
            const int arc = 24, ring = 10; var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (int i = 0; i <= arc; i++)
            {
                float angle = Mathf.Lerp(-110, 110, (float)i / arc) * Mathf.Deg2Rad;
                var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                for (int j = 0; j <= ring; j++)
                {
                    float a = j * Mathf.PI * 2 / ring;
                    vertices.Add(radial * (.16f + .027f * Mathf.Cos(a)) + Vector3.forward * (.027f * Mathf.Sin(a)));
                    uv.Add(new Vector2((float)i / arc, (float)j / ring));
                }
            }
            for (int i = 0; i < arc; i++)
                for (int j = 0; j < ring; j++)
                { int a = i * (ring + 1) + j, b = a + ring + 1; triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); }
            var mesh = new Mesh { name = "CoolingTeaHandle" }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Material Material(Shader shader, string name, Color color)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", .1f); result.SetFloat("_Smoothness", .7f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var node = new GameObject(name); node.transform.SetParent(root.transform, false); node.transform.localPosition = position; node.transform.localScale = scale;
            node.AddComponent<MeshFilter>().sharedMesh = mesh; node.AddComponent<MeshRenderer>().sharedMaterial = material; return node;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_COOLING_TEA_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
