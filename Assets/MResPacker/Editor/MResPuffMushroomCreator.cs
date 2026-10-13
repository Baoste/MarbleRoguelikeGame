using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    public static class MResPuffMushroomCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/PuffMushroom.asset";
        [MenuItem("Tools/MRes/Create or Locate Puff Mushroom Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "PuffMushroomAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Puff mushroom authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/PuffMushroomShaderRegistry.asset");
            var purple = Material(shader, "PuffMushroomPurple", new Color(.47f, .16f, .66f, 1));
            var cream = Material(shader, "PuffMushroomCream", new Color(.93f, .85f, .62f, 1));
            var dark = Material(shader, "PuffMushroomDark", new Color(.11f, .035f, .18f, 1));
            var sphere = Mesh(PrimitiveType.Sphere, "PuffMushroomSphere");
            var cylinder = Mesh(PrimitiveType.Cylinder, "PuffMushroomCylinder");
            var root = new GameObject("PuffMushroom"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "Stem", cylinder, cream, new Vector3(0, .12f, -.07f), new Vector3(.22f, .12f, .22f));
                Visual(root, "Gills", sphere, cream, new Vector3(0, .21f, -.02f), new Vector3(.67f, .12f, .53f));
                Visual(root, "Cap", sphere, purple, new Vector3(0, .29f, -.02f), new Vector3(.8f, .3f, .64f));
                for (int i = 0; i < 5; i++)
                {
                    float angle = i * Mathf.PI * 2 / 5;
                    Visual(root, "Spot" + i, sphere, cream, new Vector3(Mathf.Cos(angle) * .22f, .414f, -.02f + Mathf.Sin(angle) * .15f), new Vector3(.1f, .025f, .085f));
                }
                for (int i = 0; i < 8; i++)
                {
                    // Prefab +Z is uphill at the normal board orientation; Lua computes board-up at runtime.
                    var direction = Quaternion.AngleAxis(-60 + i * (120f / 7), Vector3.up) * Vector3.forward;
                    var rotation = Quaternion.FromToRotation(Vector3.up, direction);
                    var center = Vector3.up * .1f;
                    var pipe = Visual(root, "Outlet" + i, cylinder, purple, center + direction * .42f, new Vector3(.095f, .17f, .095f));
                    pipe.transform.localRotation = rotation;
                    var rim = Visual(root, "Rim" + i, cylinder, cream, center + direction * .58f, new Vector3(.12f, .018f, .12f));
                    rim.transform.localRotation = rotation;
                    var opening = Visual(root, "Opening" + i, cylinder, dark, center + direction * .602f, new Vector3(.078f, .004f, .078f));
                    opening.transform.localRotation = rotation;
                }
                var trigger = root.AddComponent<BoxCollider>(); trigger.isTrigger = true;
                trigger.center = new Vector3(0, .175f, -.03f); trigger.size = new Vector3(.5f, .35f, .5f);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/PuffMushroom.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/PuffMushroom.lua");
            profile.Metadata = new MResMetadataData { name = "大喷菇", description = "弹珠进入后均分为8颗小球，每颗价值和体积各为原球1/8，继承累计装置倍率，保持入口速度大小，从棋盘上方120°扇形八口均匀射出。成功喷发扣1耐久，后代再次经过同一台时直接穿过。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/PuffMushroom.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/PuffMushroom.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "PuffMushroomAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Mesh Mesh(PrimitiveType type, string name)
        {
            var primitive = GameObject.CreatePrimitive(type); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = name; AssetDatabase.CreateAsset(mesh, MResExampleCreator.Root + "/Models/" + name + ".asset"); return mesh;
        }
        private static Material Material(Shader shader, string name, Color color)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color); result.SetFloat("_Smoothness", .35f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false); result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_PUFF_MUSHROOM_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
