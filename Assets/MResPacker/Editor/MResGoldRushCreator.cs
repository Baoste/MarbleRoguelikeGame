using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Editable timber mine entrance, rails and parked cart; all outcome rules live in Lua.</summary>
    public static class MResGoldRushCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/GoldRush.asset";
        [MenuItem("Tools/MRes/Create or Locate Gold Rush Device")]
        public static void Locate()
        { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }

        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) { MResExampleCreator.EnsureScene(existing.SourcePrefab, "GoldRushAuthoring"); return existing; }
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Gold Rush authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/GoldRushShaderRegistry.asset");
            var stone = Material(shader, "GoldRushStone", new Color(.32f, .3f, .27f, 1), 0);
            var wood = Material(shader, "GoldRushTimber", new Color(.52f, .27f, .09f, 1), 0);
            var iron = Material(shader, "GoldRushIron", new Color(.18f, .2f, .22f, 1), .65f);
            var dark = Material(shader, "GoldRushTunnel", new Color(.025f, .018f, .014f, 1), 0);
            var cube = Mesh(PrimitiveType.Cube, "GoldRushCube");
            var cylinder = Mesh(PrimitiveType.Cylinder, "GoldRushWheel");
            var root = new GameObject("GoldRush"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root, "RockLeft", cube, stone, new Vector3(-.31f, .2f, .08f), new Vector3(.14f, .4f, .42f));
                Visual(root, "RockRight", cube, stone, new Vector3(.31f, .2f, .08f), new Vector3(.14f, .4f, .42f));
                Visual(root, "RockRoof", cube, stone, new Vector3(0, .42f, .08f), new Vector3(.76f, .08f, .42f));
                Visual(root, "TunnelShadow", cube, dark, new Vector3(0, .2f, .24f), new Vector3(.5f, .39f, .025f));
                Visual(root, "TimberLeft", cube, wood, new Vector3(-.235f, .2f, -.15f), new Vector3(.065f, .4f, .08f));
                Visual(root, "TimberRight", cube, wood, new Vector3(.235f, .2f, -.15f), new Vector3(.065f, .4f, .08f));
                Visual(root, "TimberBeam", cube, wood, new Vector3(0, .385f, -.15f), new Vector3(.57f, .075f, .09f));
                for (int i = 0; i < 4; i++)
                    Visual(root, "Sleeper" + i, cube, wood, new Vector3(0, .015f, -.4f + i * .17f), new Vector3(.36f, .025f, .045f));
                for (int side = -1; side <= 1; side += 2)
                    Visual(root, "Rail" + side, cube, iron, new Vector3(side * .115f, .036f, -.12f), new Vector3(.022f, .025f, .65f));
                var cart = new GameObject("Minecart"); cart.transform.SetParent(root.transform, false);
                cart.transform.localPosition = new Vector3(0, .07f, -.3f);
                Visual(cart, "Tray", cube, iron, new Vector3(0, .055f, 0), new Vector3(.27f, .055f, .23f));
                foreach (int side in new[] { -1, 1 })
                {
                    Visual(cart, "Side" + side, cube, wood, new Vector3(side * .125f, .12f, 0), new Vector3(.035f, .1f, .23f));
                    Visual(cart, "End" + side, cube, wood, new Vector3(0, .12f, side * .105f), new Vector3(.25f, .1f, .025f));
                    foreach (int axle in new[] { -1, 1 })
                    {
                        var wheel = Visual(cart, "Wheel" + side + "_" + axle, cylinder, iron,
                            new Vector3(side * .145f, 0, axle * .075f), new Vector3(.085f, .016f, .085f));
                        wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    }
                }
                var sensor = root.AddComponent<BoxCollider>(); sensor.isTrigger = true;
                sensor.center = new Vector3(0, .12f, -.12f); sensor.size = new Vector3(.55f, .24f, .58f);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/GoldRush.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile = ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab = prefab; profile.LuaScript = AssetDatabase.LoadMainAssetAtPath(folder + "/Lua/GoldRush.lua");
            profile.Metadata = new MResMetadataData { name = "淘金热", description = "矿车运走一颗弹珠，10秒后等概率：返还弹珠并获得2金币、消耗弹珠获得5金币、或一无所获。运输期间不接收新弹珠，出结果扣1耐久。", value = 120, purchasePrice = 5, rarity = "common", durability = 20, type = "device" };
            profile.ShaderRegistry = registry; profile.OutputPath = "Assets/MResPacker/Output/GoldRush.mres";
            profile.ExtraFiles = new[] { new MResExtraFile { VirtualPath = "game/device.json",
                Source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/Data/GoldRush.device.json") } };
            AssetDatabase.CreateAsset(profile, ProfilePath); MResExampleCreator.EnsureScene(prefab, "GoldRushAuthoring"); AssetDatabase.SaveAssets(); return profile;
        }
        private static Mesh Mesh(PrimitiveType primitiveType, string name)
        {
            var primitive = GameObject.CreatePrimitive(primitiveType); Mesh mesh;
            try { mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh); }
            finally { UnityEngine.Object.DestroyImmediate(primitive); }
            mesh.name = name; AssetDatabase.CreateAsset(mesh, MResExampleCreator.Root + "/Models/" + name + ".asset"); return mesh;
        }
        private static Material Material(Shader shader, string name, Color color, float metallic)
        {
            var result = new Material(shader) { name = name }; result.SetColor("_BaseColor", color);
            result.SetFloat("_Metallic", metallic); result.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(result, MResExampleCreator.Root + "/Materials/" + name + ".mat"); return result;
        }
        private static GameObject Visual(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var result = new GameObject(name); result.transform.SetParent(root.transform, false);
            result.transform.localPosition = position; result.transform.localScale = scale;
            result.AddComponent<MeshFilter>().sharedMesh = mesh; result.AddComponent<MeshRenderer>().sharedMaterial = material; return result;
        }
        public static void CreateAndBuild() => Debug.Log("MRES_GOLD_RUSH_BUILD_PASSED: " + MResBuildPipeline.Build(Create()));
    }
}
