using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MResPacker
{
    /// <summary>Authoring assets only. Continuous force and area selection live in the package's Lua.</summary>
    public static class MResAccelerationFieldCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/AccelerationField.asset";

        [MenuItem("Tools/MRes/Create or Locate Acceleration Field Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "AccelerationFieldAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Acceleration field authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/AccelerationFieldShaderRegistry.asset");
            var fieldMaterial = new Material(shader) { name = "AccelerationFieldGlass", renderQueue = (int)RenderQueue.Transparent };
            fieldMaterial.SetColor("_BaseColor", new Color(.1f, .75f, 1, .2f));
            fieldMaterial.SetFloat("_Surface", 1); fieldMaterial.SetFloat("_Blend", 0);
            fieldMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fieldMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            fieldMaterial.SetFloat("_ZWrite", 0); fieldMaterial.SetFloat("_Smoothness", .6f);
            fieldMaterial.SetOverrideTag("RenderType", "Transparent"); fieldMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            AssetDatabase.CreateAsset(fieldMaterial, folder + "/Materials/AccelerationFieldGlass.mat");
            var edgeMaterial = new Material(shader) { name = "AccelerationFieldCyan" };
            edgeMaterial.SetColor("_BaseColor", new Color(.1f, .8f, 1, 1)); edgeMaterial.SetFloat("_Smoothness", .5f);
            edgeMaterial.SetColor("_EmissionColor", new Color(.05f, .4f, .6f)); edgeMaterial.EnableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(edgeMaterial, folder + "/Materials/AccelerationFieldCyan.mat");

            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = "AccelerationFieldBox";
            AssetDatabase.CreateAsset(mesh, folder + "/Models/AccelerationFieldBox.asset");
            var root = new GameObject("AccelerationField"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var field = Box(root.transform, "Field", mesh, fieldMaterial, new Vector3(0, .14f, 0), new Vector3(.4f, .24f, 1.2f));
                field.AddComponent<BoxCollider>().isTrigger = true;
                Box(root.transform, "LeftBorder", mesh, edgeMaterial, new Vector3(-.2f, .02f, 0), new Vector3(.012f, .02f, 1.2f));
                Box(root.transform, "RightBorder", mesh, edgeMaterial, new Vector3(.2f, .02f, 0), new Vector3(.012f, .02f, 1.2f));
                Box(root.transform, "EntryBorder", mesh, edgeMaterial, new Vector3(0, .02f, .6f), new Vector3(.4f, .02f, .012f));
                Box(root.transform, "ExitBorder", mesh, edgeMaterial, new Vector3(0, .02f, -.6f), new Vector3(.4f, .02f, .012f));
                for (int i = 0; i < 3; i++)
                {
                    float z = .34f - i * .34f;
                    var a = Box(root.transform, "Arrow" + i + "Left", mesh, edgeMaterial, new Vector3(-.045f, .04f, z), new Vector3(.016f, .012f, .13f));
                    var b = Box(root.transform, "Arrow" + i + "Right", mesh, edgeMaterial, new Vector3(.045f, .04f, z), new Vector3(.016f, .012f, .13f));
                    a.transform.localRotation = Quaternion.Euler(0, -45, 0); b.transform.localRotation = Quaternion.Euler(0, 45, 0);
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/AccelerationField.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab;
            profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/AccelerationField.lua");
            profile.Metadata = new MResMetadataData { name = "加速力场", description = "在长方形区域内，持续沿箭头方向对弹珠施加恒力。", value = 120, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/AccelerationField.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/AccelerationField.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath);
            MResExampleCreator.EnsureScene(prefab, "AccelerationFieldAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static GameObject Box(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = scale;
            obj.AddComponent<MeshFilter>().sharedMesh = mesh; obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            return obj;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_ACCELERATION_FIELD_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
