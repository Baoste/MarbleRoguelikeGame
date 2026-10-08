using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MarblesECS.PhysX;

namespace MarblesECS.Editor.Diegetic
{
    public static class DiegeticUIProbe
    {
        public const string Work = "E:/毕设/MarbleRoguelikeGame/.utmp/diegetic_ui_20261008";
        [Serializable] public class Item { public string path, name, parent, mesh; public string[] materials, components; public Vector3 pos, scale, min, max; public Quaternion rot; public bool active; }
        [Serializable] public class ScriptInfo { public string path,type,json; }
        [Serializable] public class Report { public List<Item> objects=new List<Item>(); public List<ScriptInfo> scripts=new List<ScriptInfo>(); public string camera; public float fov; }
        public static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        public static void Run()
        {
            Directory.CreateDirectory(Work);
            var s=EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity",OpenSceneMode.Single);
            var report=new Report();
            foreach(var t in s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))
            {
                var r=t.GetComponent<Renderer>(); var mf=t.GetComponent<MeshFilter>();
                report.objects.Add(new Item { path=PathOf(t),name=t.name,parent=t.parent?PathOf(t.parent):"",pos=t.position,rot=t.rotation,scale=t.lossyScale,active=t.gameObject.activeInHierarchy,
                    mesh=mf&&mf.sharedMesh?AssetDatabase.GetAssetPath(mf.sharedMesh):"", materials=r?r.sharedMaterials.Select(m=>m?AssetDatabase.GetAssetPath(m):"").ToArray():Array.Empty<string>(),
                    min=r?r.bounds.min:Vector3.zero,max=r?r.bounds.max:Vector3.zero,components=t.GetComponents<Component>().Select(c=>c?c.GetType().FullName:"MISSING").ToArray() });
                foreach(var c in t.GetComponents<MonoBehaviour>())
                    if(c&&(c is MarbleGameController||c is ScoreListener||c is DevicePlacementUI||c is OwnedDrugPanelUI||c is OwnedDevicePanelUI||c is CashOutButtonUI||c is ShopUI))
                        report.scripts.Add(new ScriptInfo{path=PathOf(t),type=c.GetType().FullName,json=EditorJsonUtility.ToJson(c)});
            }
            var cam=Camera.main;report.camera=cam?PathOf(cam.transform):"";report.fov=cam?cam.fieldOfView:0;
            File.WriteAllText(Work+"/unity_inventory.json",JsonUtility.ToJson(report,true));
            if(cam)Capture(cam,Work+"/previews/00_Unity_before.png",1920,1080);
            Debug.Log("DIEGETIC_PROBE_DONE "+report.objects.Count);
        }
        [Serializable] public class PointSet { public string name; public List<Vector3> points=new List<Vector3>(); public List<Vector2> uv=new List<Vector2>(); }
        [Serializable] public class Fits { public List<PointSet> sets=new List<PointSet>(); }
        public static void ExportFit()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity",OpenSceneMode.Single);
            var all=UnityEngine.Object.FindObjectsOfType<MeshFilter>(); var report=new Fits();
            foreach(var mf in all.Where(m=>m.name.StartsWith("v1") || (m.transform.root.name=="MarbleMachine" && m.sharedMesh)))
            {
                var p=new PointSet{name=mf.name}; var vs=mf.sharedMesh.vertices; var uv=mf.sharedMesh.uv;
                for(int i=0;i<vs.Length;i++){p.points.Add(mf.transform.TransformPoint(vs[i]));p.uv.Add(uv.Length>i?uv[i]:Vector2.zero);}
                report.sets.Add(p);
            }
            File.WriteAllText(Work+"/unity_fit.json",JsonUtility.ToJson(report));
            Debug.Log("FIT_EXPORTED "+report.sets.Count);
        }
        public static void Capture(Camera camera,string path,int w,int h)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);rt.Create();var old=camera.targetTexture;var active=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
            camera.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
