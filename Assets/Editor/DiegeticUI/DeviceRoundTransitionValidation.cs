using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MarblesECS;
using MarblesECS.PhysX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarblesECS.Editor.Diegetic
{
    [InitializeOnLoad]
    public static class DeviceRoundTransitionValidation
    {
        const string Key="DeviceRoundTransitionValidation.Pending";
        const string Work="E:/毕设/MarbleRoguelikeGame/.utmp/device_round2_20261008";
        static IEnumerator sequence;
        static DiegeticMachineUI rig;
        static SimulationContext context;
        static double started;
        static readonly List<string> report=new List<string>();
        static readonly List<string> errors=new List<string>();
        static DeviceRoundTransitionValidation()
        {
            EditorApplication.playModeStateChanged+=s=>
            {
                if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false))
                {SessionState.SetBool(Key,false);EditorApplication.delayCall+=Start;}
            };
        }
        [MenuItem("Marbles ECS/实体UI/验证跨回合购买与鼠标点击")]
        public static void Run()
        {
            Directory.CreateDirectory(Work);
            EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity",OpenSceneMode.Single);
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Start()
        {
            report.Clear();errors.Clear();started=EditorApplication.timeSinceStartup;
            Application.logMessageReceived+=Log;sequence=Scenario();EditorApplication.update+=Tick;
        }
        static void Log(string text,string trace,LogType type)
        {if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-started>90)throw new Exception("Round transition validation timed out");
                if(sequence.MoveNext())return;
                Check(errors.Count==0,"No runtime errors: "+string.Join(";",errors));
                File.WriteAllLines(Work+"/passed.txt",report);Debug.Log("ROUND_TRANSITION_PASSED "+report.Count);Finish(0);
            }
            catch(Exception e){File.WriteAllLines(Work+"/failure.txt",report.Concat(new[]{e.ToString()}));Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code)
        {EditorApplication.update-=Tick;Application.logMessageReceived-=Log;sequence=null;if(Application.isBatchMode)EditorApplication.Exit(code);}
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);report.Add("PASS "+message);}
        static IEnumerator Wait(float seconds)
        {float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)yield return null;}
        static List<RaycastResult> Hits(Vector2 point)
        {
            Canvas.ForceUpdateCanvases();var hits=new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);return hits;
        }
        static void ClickVisible(GameObject target)
        {
            var point=rig.ViewCamera.WorldToScreenPoint(target.transform.position);var hits=Hits(point);
            report.Add("Raycast "+target.name+": "+string.Join(" > ",hits.Select(h=>DiegeticUIProbe.PathOf(h.gameObject.transform))));
            Check(hits.Count>0 && (hits[0].gameObject==target || hits[0].gameObject.transform.IsChildOf(target.transform)),"Top mouse hit reaches "+target.name);
            var e=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,e,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,e,ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,e,ExecuteEvents.pointerClickHandler);
        }
        static IEnumerator Scenario()
        {
            rig=UnityEngine.Object.FindObjectOfType<DiegeticMachineUI>();
            while(!rig.Controller.IsReady)yield return null;
            var simulation=(MarbleSimulation)typeof(MarbleGameController).GetField("simulation",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(rig.Controller);
            context=simulation.Context;
            var result=UnityEngine.Object.FindObjectOfType<RandomScoreResultUI>();
            Check(result && result.ResultText,"Actual settlement UI is present");
            if(rig.Controller.Session.Phase==RoundPhase.Build)Check(rig.Controller.BeginRound(),"Start first round");
            for(int stage=1;stage<=2;stage++)
            {
            // Seed an achievable score and non-empty gamble pool, then use the real settlement/shop transitions.
            var round=context.Round;round.Score=Math.Max(0,round.TargetScore-context.Campaign.TotalScore);round.PendingScore=22;context.Round=round;
            Check(rig.Controller.CashOut(),"Cash out round "+stage+" through controller");
            while(rig.Controller.Session.Phase!=RoundPhase.Shop)yield return null;
            var wait=Wait(.4f);while(wait.MoveNext())yield return null;
            Check(rig.Controller.Session.StageIndex==stage,"Real settlement advances to stage "+(stage+1));
            report.Add("Settlement text enabled="+result.ResultText.enabled+" raycastTarget="+result.ResultText.raycastTarget+" rect="+result.ResultText.rectTransform.rect);
            Check(result.ResultText.enabled && !result.ResultText.raycastTarget,"Result is visible after reveal, without intercepting mouse input");
            var magnet=context.Balance.Devices.First(d=>d.Kind==DeviceKind.Magnet);
            // Deterministic shop stock; purchase still uses the production wallet/inventory path.
            var offers=context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);offers.Clear();
            offers.Add(new ShopOfferData{Kind=ShopItemKind.Device,DefinitionId=magnet.Id,Price=1});
            Check(rig.Controller.BuyOffer(0),"Buy the reported magnetic device in the shop");
            int id=rig.Controller.GetOwnedDevices().First(d=>d.DefinitionId==magnet.Id&&!d.Placed).InstanceId;
            Check(rig.Controller.EnterBuild(),"Continue from shop to Build "+(stage+1));
            wait=Wait(.85f);while(wait.MoveNext())yield return null;
            Check(rig.IsBoardOpen && rig.ItemGroup.interactable,"Sliding board is open and interactive");
            Check(!result.ResultText.enabled,"Old result clears before the next Build");
            report.Add("Paused="+rig.Controller.Paused+" selected="+rig.Placement.SelectedInstanceId);
            var card=rig.DeviceButtons.First(b=>b.gameObject.activeSelf && rig.DeviceNames[Array.IndexOf(rig.DeviceButtons,b)].text==magnet.Name);
            ClickVisible(card.gameObject);
            Check(rig.Placement.SelectedInstanceId==id,"Clicking the bought card selects its real instance");
            var probe=Hits(rig.ViewCamera.WorldToScreenPoint(rig.Placement.PlacementPlane.position));
            report.Add("Playfield hits: "+string.Join(" > ",probe.Select(h=>DiegeticUIProbe.PathOf(h.gameObject.transform))));
            Check(probe.Count==0,"Playfield is not blocked by an invisible UI surface");
            foreach(int width in new[]{1920,3840})
            {
                var cam=rig.ViewCamera;int height=width*9/16;
                var texture=new RenderTexture(width,height,24);texture.Create();
                var oldTarget=cam.targetTexture;cam.targetTexture=texture;cam.aspect=16f/9f;
                var overlays=UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
                try
                {
                    foreach(var c in overlays){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=cam;c.planeDistance=1;}
                    rig.Placement.CancelPlacement();
                    // Even if an old animation leaves the result visible, it cannot block the card.
                    result.ResultText.enabled=true;
                    ClickVisible(card.gameObject);
                    Check(rig.Placement.SelectedInstanceId==id,$"{width}x{height} card selects purchased device with result overlay visible");
                    Check(Hits(cam.WorldToScreenPoint(rig.Placement.PlacementPlane.position)).Count==0,$"{width}x{height} placement area receives mouse input");
                    result.ResultText.enabled=false;
                    Canvas.ForceUpdateCanvases();
                    DiegeticUIProbe.Capture(cam,Work+$"/Build_{stage+1}_{width}.png",width,height);
                }
                finally
                {
                    result.ResultText.enabled=false;
                    foreach(var c in overlays)c.renderMode=RenderMode.ScreenSpaceOverlay;
                    cam.targetTexture=oldTarget;cam.ResetAspect();texture.Release();UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            bool placed=false;
            var plane=rig.Placement.PlacementPlane;
            for(float x=-2.5f;x<=2.5f&&!placed;x+=.4f)
                for(float z=-3f;z<=3f&&!placed;z+=.4f)
                {
                    var world=plane.position+plane.rotation*new Vector3(x,rig.Placement.DevicePlaneDistance,z);
                    if(Hits(rig.ViewCamera.WorldToScreenPoint(world)).Count!=0)continue;
                    var local=rig.Placement.BoardRoot.InverseTransformPoint(world);
                    var scale=rig.Placement.BoardRoot.lossyScale;
                    placed=rig.Controller.PlaceDevice(id,local.x,local.z,world,Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z)));
                }
            Check(placed,"Purchased magnetic device passes actual placement and collision rules");
            rig.Placement.CancelPlacement();rig.RefreshInventory();
            Check(rig.Controller.GetOwnedDevices().Single(d=>d.InstanceId==id).Placed,"Placement is stored on the purchased instance");
            wait=Wait(.2f);while(wait.MoveNext())yield return null;
            Check(UnityEngine.Object.FindObjectsOfType<MarbleDevice>().Any(d=>d.InstanceId==id&&d.enabled),"Purchased magnetic device has its active gameplay component");
            ClickVisible(rig.Continue.gameObject);
            Check(rig.Controller.Session.Phase==RoundPhase.Playing && !rig.Controller.Paused,"Physical Continue starts the next playable round");
            wait=Wait(.8f);while(wait.MoveNext())yield return null;
            Check(!result.ResultText.enabled && !rig.ItemGroup.blocksRaycasts && rig.ItemGroup.alpha==0,"Result and board UI release input in Playing");
            Check(rig.Controller.GetOwnedDevices().Single(d=>d.InstanceId==id).Placed,"Placed device survives the next round start");
            }
        }
    }
}
