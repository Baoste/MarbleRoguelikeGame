using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable socket and three tooth appearances; progression is entirely authored in Lua.</summary>
    public static class MResDentalImplantCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/DentalImplant.asset";
        [MenuItem("Tools/MRes/Create or Locate Dental Implant Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "DentalImplantAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Dental implant authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/DentalImplantShaderRegistry.asset");
            var socket = Material(shader, "DentalImplantSocket", new Color(.52f, .27f, .18f, 1), .15f);
            var dark = Material(shader, "DentalImplantHole", new Color(.035f, .025f, .03f, 1), 0);
            var steel = Material(shader, "DentalImplantSteel", new Color(.55f, .63f, .69f, 1), .9f);
            var ceramic = Material(shader, "DentalImplantCeramic", new Color(.98f, .94f, .83f, 1), .05f);
            var diamond = Material(shader, "DentalImplantDiamond", new Color(.25f, .88f, 1, 1), .65f);
            Mesh cylinder = PrimitiveMesh(PrimitiveType.Cylinder), sphere = PrimitiveMesh(PrimitiveType.Sphere);
            var root = new GameObject("DentalImplant"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var hull = Visual(root, "Socket", cylinder, socket, new Vector3(0, .045f, 0), new Vector3(.56f, .045f, .56f));
                var collider = hull.AddComponent<MeshCollider>(); collider.sharedMesh = cylinder; collider.convex = true;
                Visual(root, "Hole", cylinder, dark, new Vector3(0, .092f, 0), new Vector3(.4f, .005f, .4f));
                Tooth(root, "Steel", cylinder, sphere, steel);
                Tooth(root, "Ceramic", cylinder, sphere, ceramic);
                Tooth(root, "Diamond", cylinder, sphere, diamond);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/DentalImplant.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/DentalImplant.lua");
            profile.Metadata = new MResMetadataData { name = "种植牙", description = "首次放置为无效果的洞；向其拖入一件背包中的种植牙可逐级升级。不锈钢牙每次撞击增加2点累计倍率、耐久5；陶瓷牙增加5点、耐久8；金刚钻牙增加20点、耐久20。每次有效撞击扣1，耗尽立即退回洞。升级补满耐久，最高阶段不再接受；阶段跨回合保留，新回合补满当前阶段耐久。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/DentalImplant.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json", Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/DentalImplant.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "DentalImplantAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var primitive = GameObject.CreatePrimitive(type); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "DentalImplant" + type;
            AssetDatabase.CreateAsset(mesh, MResExampleCreator.Root + "/Models/" + mesh.name + ".asset"); return mesh;
        }
        private static void Tooth(GameObject root, string name, Mesh cylinder, Mesh sphere, Material material)
        {
            var group = new GameObject(name); group.transform.SetParent(root.transform, false);
            Visual(group, "Root", cylinder, material, new Vector3(0, .16f, 0), new Vector3(.23f, .09f, .23f));
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Visual(group, "Cusp" + x + "_" + z, sphere, material, new Vector3(x * .055f, .27f, z * .055f), new Vector3(.19f, .18f, .19f));
            foreach (var renderer in group.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        }
        private static Material Material(Shader shader, string name, Color color, float metallic)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", .65f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_DENTAL_IMPLANT_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
