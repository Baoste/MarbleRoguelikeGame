using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    public static class MResBilliardsCreator
    {
        public const string ProfilePath = MResExampleCreator.Root + "/Profiles/Billiards.asset";
        [MenuItem("Tools/MRes/Create or Locate Billiards Device")]
        public static void Locate() { Selection.activeObject = Create(); EditorGUIUtility.PingObject(Selection.activeObject); }
        public static MResBuildProfile Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MResBuildProfile>(ProfilePath);
            if (existing != null) return existing;
            const string folder = MResExampleCreator.Root;
            foreach (string sub in new[] { "Models", "Materials", "Prefabs", "Profiles", "Scenes" }) Directory.CreateDirectory(folder + "/" + sub);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Billiards authoring requires URP/Lit.");
            var registry = ScriptableObject.CreateInstance<MResPackerShaderRegistry>();
            registry.Entries = new[] { new MResPackerShaderRegistry.Entry { Shader = shader, ShaderId = "device-lit" } };
            AssetDatabase.CreateAsset(registry, folder + "/Profiles/BilliardsShaderRegistry.asset");
            var green = Mat(shader, "BilliardsFelt", new Color(.025f,.42f,.22f));
            var wood = Mat(shader, "BilliardsWood", new Color(.25f,.1f,.04f));
            var dark = Mat(shader, "BilliardsPocket", new Color(.015f,.015f,.015f));
            var white = Mat(shader, "BilliardsBall", new Color(.92f,.88f,.7f));
            var cube = Mesh(PrimitiveType.Cube); var sphere = Mesh(PrimitiveType.Sphere); var cylinder = Mesh(PrimitiveType.Cylinder);
            var root = new GameObject("Billiards"); GameObject prefab;
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Visual(root,"Frame",cube,wood,new Vector3(0,.08f,0),new Vector3(.6f,.16f,.58f));
                Visual(root,"Table",cube,green,new Vector3(0,.17f,0),new Vector3(.51f,.04f,.48f));
                Visual(root,"Pocket",cylinder,dark,new Vector3(0,.195f,.08f),new Vector3(.23f,.01f,.23f));
                Visual(root,"Outlet",cube,dark,new Vector3(0,.1f,-.295f),new Vector3(.23f,.13f,.02f));
                Visual(root,"WhiteBall",sphere,white,new Vector3(-.17f,.23f,-.14f),Vector3.one*.09f);
                Visual(root,"BlackBall",sphere,dark,new Vector3(.17f,.23f,-.14f),Vector3.one*.09f);
                var sensor=root.AddComponent<BoxCollider>(); sensor.isTrigger=true; sensor.center=new Vector3(0,.15f,0);sensor.size=new Vector3(.55f,.4f,.52f);
                prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prefabs/Billiards.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var profile=ScriptableObject.CreateInstance<MResBuildProfile>();
            profile.SourcePrefab=prefab; profile.LuaScript=AssetDatabase.LoadMainAssetAtPath(folder+"/Lua/Billiards.lua");
            profile.Metadata=new MResMetadataData {name="台球",description="吞入一球后吐出一颗编号台球，保留原球价值。黑八概率20%，1至7号均分80%。进入得分区时黑八使RUSH条减100，其他编号增加编号×10，替代普通贡献结算。吐球后冷却15秒，成功吐出扣1耐久。RUSH期间不积累条值，黑八不终止已激活的RUSH。",value=120,purchasePrice=5,rarity="common",durability=20,type="device"};
            profile.ShaderRegistry=registry;profile.OutputPath="Assets/MResPacker/Output/Billiards.mres";
            profile.ExtraFiles=new[]{new MResExtraFile{VirtualPath="game/device.json",Source=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"/Data/Billiards.device.json")}};
            AssetDatabase.CreateAsset(profile,ProfilePath);MResExampleCreator.EnsureScene(prefab,"BilliardsAuthoring");AssetDatabase.SaveAssets();return profile;
        }
        private static Material Mat(Shader shader,string name,Color color)
        {var m=new Material(shader){name=name};m.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(m,MResExampleCreator.Root+"/Materials/"+name+".mat");return m;}
        private static Mesh Mesh(PrimitiveType type)
        {var go=GameObject.CreatePrimitive(type);Mesh mesh;try{mesh=UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);}finally{UnityEngine.Object.DestroyImmediate(go);}mesh.name="Billiards"+type;AssetDatabase.CreateAsset(mesh,MResExampleCreator.Root+"/Models/"+mesh.name+".asset");return mesh;}
        private static void Visual(GameObject root,string name,Mesh mesh,Material material,Vector3 position,Vector3 scale)
        {var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.transform.localPosition=position;go.transform.localScale=scale;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;}
        public static void CreateAndBuild()=>Debug.Log("MRES_BILLIARDS_BUILD_PASSED: "+MResBuildPipeline.Build(Create()));
    }
}
