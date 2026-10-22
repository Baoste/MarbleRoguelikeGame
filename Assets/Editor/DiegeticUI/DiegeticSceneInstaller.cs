using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using MarblesECS.PhysX;

namespace MarblesECS.Editor.Diegetic
{
    public static class DiegeticSceneInstaller
    {
        const string W=DiegeticUIProbe.Work;
        const string Art="Assets/Art/DiegeticUI";
        const string SourceData="Tools/DiegeticUI/SourceExport";
        [Serializable] public class Piece {public string name,group,file,material;public float[] center,sourceCenter;public bool hidden;}
        [Serializable] public class Fit {public string group;public float[] m;}
        [Serializable] public class Manifest {public Piece[] objects;public Fit[] transforms;}
        static Manifest source;
        static readonly Dictionary<string,Transform> groups=new Dictionary<string,Transform>();
        static readonly Dictionary<string,Transform> pieces=new Dictionary<string,Transform>();
        static Vector3 V(float[] a)=>new Vector3(a[0],a[1],a[2]);
        static Vector3 P(string g,Vector3 p)
        {
            var m=source.transforms.First(f=>f.group==g).m;
            return new Vector3(p.x*m[0]+p.y*m[3]+p.z*m[6]+m[9],p.x*m[1]+p.y*m[4]+p.z*m[7]+m[10],p.x*m[2]+p.y*m[5]+p.z*m[8]+m[11]);
        }
        static Vector3 D(string g,Vector3 d)=>(P(g,d)-P(g,Vector3.zero)).normalized;
        static Transform Anchor(string name,Transform parent,Vector3 worldPosition,Quaternion rotation)
        {
            var t=new GameObject(name).transform;t.SetParent(parent,false);t.position=worldPosition;t.rotation=rotation;return t;
        }
        static Quaternion Face(string g,Vector3 right,Vector3 up)
        {var r=D(g,right);var u=D(g,up);return Quaternion.LookRotation(Vector3.Cross(r,u),u);}

        [MenuItem("Marbles ECS/实体UI/安装到 TestScene")]
        public static void Install()
        {
            Directory.CreateDirectory(Art+"/Meshes");Directory.CreateDirectory(Art+"/Materials");AssetDatabase.Refresh();
            // Rebuilding this installer replaces only its own generated group. The user's existing objects are retained.
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity",OpenSceneMode.Single);
            var previous=GameObject.Find("Machine_DiegeticUI");if(previous)UnityEngine.Object.DestroyImmediate(previous);
            source=JsonUtility.FromJson<Manifest>(File.ReadAllText(SourceData+"/manifest.json"));groups.Clear();pieces.Clear();
            var root=new GameObject("Machine_DiegeticUI");var rig=root.AddComponent<DiegeticMachineUI>();
            rig.Controller=UnityEngine.Object.FindObjectOfType<MarbleGameController>();rig.Placement=UnityEngine.Object.FindObjectOfType<DevicePlacementUI>();rig.ViewCamera=Camera.main;
            rig.Font=InterfaceFont();
            if(!rig.Font || !rig.Controller || !rig.ViewCamera)throw new Exception("Missing game controller, camera, or Chinese font");
            var models=new GameObject("模型_保留原有材质").transform;models.SetParent(root.transform,false);
            foreach(var p in source.objects)
            {
                if(p.group=="物品板" || p.group=="药剂架子" || p.group=="Original / 药品槽")continue; // Import the inclined panel, matching the player's view in the supplied layout.
                if(!groups.TryGetValue(p.group,out var group)){group=new GameObject(p.group).transform;group.SetParent(models,false);groups[p.group]=group;}
                var mesh=ReadMesh(SourceData+"/"+p.file);string asset=Art+"/Meshes/"+Path.GetFileNameWithoutExtension(p.file)+".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(asset);
                if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,asset);
                var go=new GameObject(p.name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(group,false);go.transform.position=V(p.center);
                go.GetComponent<MeshFilter>().sharedMesh=mesh;var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=MaterialFor(p.material);r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;
                pieces[p.group+"/"+p.name]=go.transform;
            }
            foreach(string name in new[]{"v1药品槽-合并","v1信息板-合并","v1抽血机-合并"})
            {var old=GameObject.Find(name);if(old && old.GetComponent<Renderer>())old.GetComponent<Renderer>().enabled=false;}
            // Same viewpoint and central gameplay geometry, with enough framing for the new upper/side controls.
            rig.ViewCamera.fieldOfView=38;
            rig.ViewCamera.transform.position=new Vector3(-6.182755f,18.821587f,-.9496058f)+rig.ViewCamera.transform.up*.95f;
            rig.OutlineMaterial=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/HoverOutline.mat");
            if(!rig.OutlineMaterial){rig.OutlineMaterial=new Material(Shader.Find("Marbles/Physical Hover Outline"));rig.OutlineMaterial.SetColor("_OutlineColor",new Color(1,.7f,.3f,1));rig.OutlineMaterial.SetFloat("_Width",.025f);AssetDatabase.CreateAsset(rig.OutlineMaterial,Art+"/Materials/HoverOutline.mat");}
            var anchors=new GameObject("UI锚点_可调整").transform;anchors.SetParent(root.transform,false);
            var infoRot=rig.ViewCamera.transform.rotation*Quaternion.Euler(0,0,-12);
            var scorePoints=new[]{new Vector3(5.947f,-1.76f,1.765f),new Vector3(5.943f,-1.747f,1.515f),new Vector3(5.962f,-1.735f,1.381f)};
            for(int i=0;i<3;i++)rig.ScoreAnchors[i]=Anchor(new[]{"回合分锚点","目标分锚点","总分锚点"}[i],anchors,Vector3.Lerp(P("信息板子",scorePoints[i]),rig.ViewCamera.transform.position,.08f),infoRot);
            rig.ScoreWorldSize=new Vector2(2.45f,.43f);
            // Blood display sits immediately in front of the existing top machine plaque.
            var mainMesh=GameObject.Find("MarbleMachine").GetComponentsInChildren<Renderer>();
            Vector3 bloodPos=Vector3.Lerp(new Vector3(4.014f,11.941f,-1.219f),rig.ViewCamera.transform.position,.06f);
            rig.BloodWorldSize=new Vector2(5.1f,.57f);
            rig.BloodAnchor=Anchor("血条锚点",anchors,bloodPos,rig.ViewCamera.transform.rotation);
            string aux="开始以及结束装置";
            rig.LeverPivot=Anchor("结算扳手_动画轴",groups[aux],P(aux,new Vector3(5.703f,-.081f,1.047f)),Quaternion.identity);
            foreach(string n in new[]{"Cube.055","Cube.057"})pieces[aux+"/"+n].SetParent(rig.LeverPivot,true);
            rig.LeverAxis=D(aux,Vector3.up);rig.LeverOffAngle=65;
            rig.LeverPivot.rotation=Quaternion.AngleAxis(-25,rig.LeverAxis);
            rig.LeverHighlight=Highlight(rig.LeverPivot,rig.OutlineMaterial);
            rig.LeverAnchor=Anchor("结算触控锚点",anchors,P(aux,new Vector3(5.55f,-.085f,1.10f)),rig.ViewCamera.transform.rotation);
            string board="物品板2";rig.ItemBoard=groups[board];
            var bounds=new Bounds();bool first=true;foreach(var r in rig.ItemBoard.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
            var children=rig.ItemBoard.Cast<Transform>().ToArray();foreach(var child in children)child.SetParent(models,true);
            rig.ItemBoard.position=bounds.center;foreach(var child in children)child.SetParent(rig.ItemBoard,true);
            var panelRotation=Face(board,new Vector3(0,-1,0),new Vector3(.606f,0,.796f));
            var panelCenter=P(board,new Vector3(5.254f,-1.885f,1.991f));
            rig.ItemUIAnchor=Anchor("物品板界面锚点",rig.ItemBoard,panelCenter,panelRotation);
            rig.ItemWorldSize=new Vector2(2.75f,5.75f);
            rig.ItemBoard.rotation=rig.ViewCamera.transform.rotation*Quaternion.Inverse(panelRotation);
            rig.ItemBoard.localScale=Vector3.one*.82f;
            // Position the sliding assembly as a single part; no gameplay core or collider is moved.
            float depth=rig.ViewCamera.WorldToViewportPoint(rig.ItemBoard.position).z;
            float oldAspect=rig.ViewCamera.aspect;rig.ViewCamera.aspect=16f/9f;
            var desired=rig.ViewCamera.ViewportToWorldPoint(new Vector3(.875f,.50f,depth));rig.ViewCamera.ResetAspect();
            rig.ItemBoard.position=desired;
            rig.BoardOpenPosition=rig.ItemBoard.localPosition;
            rig.BoardClosedPosition=rig.BoardOpenPosition+rig.ViewCamera.transform.right*2.4f;
            rig.ItemBoard.localPosition=rig.BoardClosedPosition;
            rig.BuildVisuals();rig.BindScoreListener();
            foreach(var name in new[]{"ScorePanel","DrugPanel","DevicePanel","BloodSlider","CashOutButton","ContinueButton"})
            {var t=GameObject.Find("Canvas").transform.Find(name);if(t)t.gameObject.SetActive(false);}
            foreach(var ui in UnityEngine.Object.FindObjectsOfType<OwnedDevicePanelUI>())ui.enabled=false;
            rig.Placement.BlockClicksOverUI=true;
            EditorUtility.SetDirty(rig);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            DiegeticUIProbe.Capture(rig.ViewCamera,W+"/previews/02_Unity_installed.png",1920,1080);
            Debug.Log("DIEGETIC_INSTALL_COMPLETE objects="+pieces.Count);
        }
        static Mesh ReadMesh(string path)
        {
            using(var r=new BinaryReader(File.OpenRead(path)))
            {
                int n=r.ReadInt32();var vs=new Vector3[n];var ns=new Vector3[n];var uv=new Vector2[n];var tris=new int[n];
                for(int i=0;i<n;i++){vs[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());ns[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());tris[i]=i;}
                var mesh=new Mesh{name=Path.GetFileNameWithoutExtension(path),indexFormat=n>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.vertices=vs;mesh.normals=ns;mesh.uv=uv;mesh.triangles=tris;mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
            }
        }
        static TMP_FontAsset InterfaceFont()
        {
            const string path=Art+"/Materials/DiegeticChineseSDF.asset";
            var asset=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);if(asset)return asset;
            var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/msyh.ttc");
            asset=TMP_FontAsset.CreateFontAsset(font,64,8,GlyphRenderMode.SDFAA,4096,4096,AtlasPopulationMode.Dynamic,true);
            asset.name="Diegetic Chinese SDF";asset.material.name="Diegetic Chinese SDF Material";
            var text=File.ReadAllText("Assets/Resources/GameContent.json")+File.ReadAllText("Assets/Resources/GameBalance.json")+File.ReadAllText("Assets/Scripts/UI/DiegeticMachineUI.cs");
            string chars=new string(text.Where(c=>!char.IsControl(c)).Distinct().ToArray());
            asset.TryAddCharacters(chars,out string missing);
            AssetDatabase.CreateAsset(asset,path);AssetDatabase.AddObjectToAsset(asset.material,asset);
            foreach(var tex in asset.atlasTextures){tex.name="Diegetic Chinese Atlas";AssetDatabase.AddObjectToAsset(tex,asset);}
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            Debug.Log("UI_FONT_CREATED glyphs="+asset.characterTable.Count+" missing="+missing);
            return asset;
        }
        static Material MaterialFor(string name)
        {
            var originals=new Dictionary<string,string>{{"Original / 信息板","xinxiban"},{"Original / 药品槽","yaopincao"},{"Original / 抽血机","chouxueji"},{"Original / 弹珠机主机","zhuji"}};
            if(originals.TryGetValue(name,out var folder))return AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/materials/{folder}/{folder}.mat");
            string target=name.Replace('/','_');
            foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/Art/Environments/BlackClinicV3/Materials"}))
            {var path=AssetDatabase.GUIDToAssetPath(guid);if(Path.GetFileNameWithoutExtension(path).EndsWith(target,StringComparison.Ordinal))return AssetDatabase.LoadAssetAtPath<Material>(path);}
            throw new Exception("Missing material mapping: "+name);
        }
        static GameObject Highlight(Transform parent,Material material)
        {
            var source=parent.GetComponentsInChildren<MeshFilter>().ToArray();var root=new GameObject("悬停轮廓");root.transform.SetParent(parent,false);
            foreach(var f in source){var g=new GameObject(f.name+"_outline",typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(f.transform,false);g.GetComponent<MeshFilter>().sharedMesh=f.sharedMesh;var r=g.GetComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;g.transform.SetParent(root.transform,true);}
            root.SetActive(false);return root;
        }
    }
}
