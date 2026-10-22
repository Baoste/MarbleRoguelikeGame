using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MarblesECS.PhysX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarblesECS.Editor.Diegetic
{
    [InitializeOnLoad]
    public static class DiegeticUIValidation
    {
        const string Key="DiegeticUIValidation.Pending";
        static IEnumerator sequence;
        static readonly List<string> passed=new List<string>();
        static DiegeticMachineUI rig;
        static SimulationContext context;
        static double started;
        static readonly List<string> runtimeErrors=new List<string>();
        static DiegeticUIValidation()
        {
            EditorApplication.playModeStateChanged+=s=>
            {
                if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false))
                {SessionState.SetBool(Key,false);EditorApplication.delayCall+=Start;}
            };
        }
        [MenuItem("Marbles ECS/实体UI/验证交互与1080p和4K")]
        public static void Run()
        {
            if(EditorApplication.isPlaying){Start();return;}
            EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity",OpenSceneMode.Single);
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Start()
        {
            passed.Clear();runtimeErrors.Clear();started=EditorApplication.timeSinceStartup;
            Application.logMessageReceived+=Log;sequence=Scenario();EditorApplication.update+=Tick;
        }
        static void Log(string text,string trace,LogType type){if(type==LogType.Exception || type==LogType.Error || type==LogType.Assert)runtimeErrors.Add(text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-started>150)throw new Exception("Diegetic validation timed out");
                if(sequence.MoveNext())return;
                Check(runtimeErrors.Count==0,"No runtime exceptions or errors: "+string.Join(";",runtimeErrors));
                File.WriteAllLines(DiegeticUIProbe.Work+"/validation_passed.txt",passed);
                Debug.Log("DIEGETIC_VALIDATION_PASSED "+passed.Count+" assertions");Finish(0);
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText(DiegeticUIProbe.Work+"/validation_failure.txt",e+"\n"+string.Join("\n",passed));Finish(1);}
        }
        static void Finish(int code)
        {EditorApplication.update-=Tick;Application.logMessageReceived-=Log;sequence=null;if(Application.isBatchMode)EditorApplication.Exit(code);}
        static void Check(bool yes,string message){if(!yes)throw new Exception(message);passed.Add(message);}
        static IEnumerator Wait(float seconds)
        {float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)yield return null;}
        static void Click(DiegeticUIHit hit)
        {ExecuteEvents.Execute(hit.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);}
        static DiegeticUIHit Hit(int kind,int index=0)=>rig.GetComponentsInChildren<DiegeticUIHit>(true).First(h=>h.Kind==kind&&h.Index==index);

        static IEnumerator Scenario()
        {
            rig=UnityEngine.Object.FindObjectOfType<DiegeticMachineUI>();Check(rig,"Rig exists in saved scene");
            while(!rig.Controller.IsReady)yield return null;
            var simulation=(MarbleSimulation)typeof(MarbleGameController).GetField("simulation",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(rig.Controller);context=simulation.Context;
            Check(rig.Controller.PhysicsRoot && rig.Placement.BoardRoot,"Original physics and placement roots remain connected");
            Check(rig.GetComponentsInChildren<Collider>(true).Length==0,"Imported UI props do not add gameplay collisions");
            Check(rig.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m && m.shader && m.shader.isSupported)),"All imported materials use supported shaders");
            for(int i=0;i<7;i++)CampaignStateUtility.CreateDevice(context,context.Balance.Devices[i%context.Balance.Devices.Length],0);
            var round=context.Round;round.Phase=(byte)RoundPhase.Build;context.Round=round;rig.RefreshInventory();
            // Restart the panel motion, covering the state immediately before its animation completes.
            rig.ItemBoard.localPosition=rig.BoardClosedPosition;
            yield return null;
            var wait=Wait(.75f);while(wait.MoveNext())yield return null;
            Check(rig.IsBoardOpen&&rig.ItemGroup.alpha==1&&rig.ItemGroup.blocksRaycasts,"Build opens board and reveals its UI");
            Check(rig.VisibleDeviceCount==6&&rig.DevicePager.activeSelf,"More than six devices paginate instead of overflowing");
            int firstId=rig.Controller.GetOwnedDevices().First(d=>!d.Placed).InstanceId;
            Click(Hit(2,0));Check(rig.Placement.SelectedInstanceId==firstId,"Physical board selection reaches existing placement logic");
            rig.ChangeDevicePage(1);Check(rig.VisibleDeviceCount>0,"Second page devices remain accessible");rig.ChangeDevicePage(-1);
            // Find a legal placement on the original plane, keeping collision rejection active.
            bool placed=false;var plane=rig.Placement.PlacementPlane?rig.Placement.PlacementPlane:rig.Placement.BoardRoot;
            for(float x=-1.4f;x<=1.4f&&!placed;x+=.35f)for(float z=-1.9f;z<=1.9f&&!placed;z+=.35f)
            {var w=plane.TransformPoint(new Vector3(x,0,z));var local=rig.Placement.BoardRoot.InverseTransformPoint(w);placed=rig.Controller.PlaceDevice(firstId,local.x,local.z,w,1);}
            Check(placed,"A selected device can still be placed on the original playfield");rig.Placement.CancelPlacement();rig.RefreshInventory();
            wait=Wait(.2f);while(wait.MoveNext())yield return null;
            Capture("03_Build_1080p",1920,1080);Capture("04_Build_4K",3840,2160);
            CheckViewport();
            Click(Hit(5));Check(rig.Controller.Session.Phase==RoundPhase.Playing,"Physical Continue starts the original round");
            yield return null;yield return null;
            Check(rig.ItemGroup.alpha==0&&!rig.ItemGroup.blocksRaycasts,"Device UI becomes hidden and non-interactive when leaving Build");
            wait=Wait(.75f);while(wait.MoveNext())yield return null;
            Check(Vector3.Distance(rig.ItemBoard.localPosition,rig.BoardClosedPosition)<.01f,"Board retracts after Build");
            Capture("05_Play_1080p",1920,1080);Capture("06_Play_4K",3840,2160);
            round=context.Round;round.Score=0;context.Round=round;
            float blood=rig.Controller.Snapshot.Blood;Click(Hit(0));Check(rig.Controller.Session.Phase==RoundPhase.Playing&&Mathf.Approximately(blood,rig.Controller.Snapshot.Blood),"Lever cannot bypass target score");
            rig.Hover(0,0,true);Check(rig.LeverHighlight.activeSelf,"Lever hover outline works");Capture("07_Lever_highlight",1920,1080);rig.ClearHover();
            rig.Controller.FireOnce();wait=Wait(.12f);while(wait.MoveNext())yield return null;Check(rig.Controller.Snapshot.Blood<blood,"The original launcher still spawns and consumes blood");
            round=context.Round;round.Score=round.TargetScore;context.Round=round;Check(rig.CanCashOut(),"Lever enables at the actual target score");
            Click(Hit(0));Check(rig.Controller.Snapshot.Blood==0&&rig.Controller.Session.Phase!=RoundPhase.Playing,"Lever closes supply and starts real cashout flow");Check(!rig.TryCashOut(),"Double activation cannot settle twice");
            // A shop purchase is executed by the real shop API; only the test offer and test funds are seeded.
            context.RemoveAllMarbles();round=context.Round;round.Phase=(byte)RoundPhase.Shop;context.Round=round;
            var campaign=context.Campaign;campaign.Coins=100000;context.Campaign=campaign;
            var offers=context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);offers.Clear();offers.Add(new ShopOfferData{Kind=ShopItemKind.Device,DefinitionId=context.Balance.Devices[0].Id,Price=1});
            Check(rig.Controller.BuyOffer(0),"Existing shop purchase succeeds");rig.RefreshInventory();Check(rig.Controller.GetOwnedDevices().Any(d=>!d.Placed),"Purchased device enters inventory");
            Check(rig.Controller.EnterBuild(),"Shop Continue still enters Build");
            yield return null;Check(rig.ItemGroup.alpha==0,"Board UI waits until slide-out has finished");
            wait=Wait(.75f);while(wait.MoveNext())yield return null;Check(rig.ItemGroup.alpha==1&&rig.IsBoardOpen,"Next Build correctly opens the board again");
            var lever=Hit(0);var drag=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=new Vector2(100,100)};
            lever.OnBeginDrag(drag);drag.position=new Vector2(100,200);lever.OnEndDrag(drag);
            Check(rig.Controller.Session.Phase==RoundPhase.Playing,"Dragging the lever upwards opens supply and starts a round");
            rig.Controller.RestartRound();wait=Wait(.75f);while(wait.MoveNext())yield return null;
            Check(!rig.Controller.RequiresRestart,"Runtime remains healthy after all interactions");
        }
        static void CheckViewport()
        {
            float oldAspect=rig.ViewCamera.aspect;rig.ViewCamera.aspect=16f/9f;
            foreach(var a in rig.ScoreAnchors.Concat(new[]{rig.BloodAnchor,rig.LeverAnchor}))
            {var v=rig.ViewCamera.WorldToViewportPoint(a.position);Check(v.z>0&&v.x>.015f&&v.x<.985f&&v.y>.015f&&v.y<.985f,"Visible physical anchor: "+a.name+" "+v);}
            rig.ViewCamera.ResetAspect();
        }
        static void Capture(string name,int w,int h)
        {
            var cam=rig.ViewCamera;var texture=new RenderTexture(w,h,24);texture.Create();var old=cam.targetTexture;cam.targetTexture=texture;cam.aspect=(float)w/h;
            var overlays=UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
            foreach(var c in overlays){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=cam;c.planeDistance=1f;}
            Canvas.ForceUpdateCanvases();cam.Render();var active=RenderTexture.active;RenderTexture.active=texture;
            var image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();File.WriteAllBytes(DiegeticUIProbe.Work+"/previews/"+name+".png",image.EncodeToPNG());
            // Check that world-space UI receives pointer raycasts at both resolutions.
            var hit=Hit(0);var p=cam.WorldToScreenPoint(hit.transform.position);var results=new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},results);
            Check(results.Any(r=>r.gameObject==hit.gameObject),$"{w}x{h} pointer raycasts reach settlement lever");
            foreach(var c in overlays)c.renderMode=RenderMode.ScreenSpaceOverlay;
            cam.targetTexture=old;cam.ResetAspect();RenderTexture.active=active;texture.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
