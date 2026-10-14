using System;
using System.Linq;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void BilliardsPhysics(float dt)
        {
            var scene=SceneManager.CreateScene("Billiards_"+Guid.NewGuid(),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation=new MarbleSimulation(Balance());var bridge=new MarblePhysicsBridge(scene,null,null,simulation.EnqueueContact);
            var board=new GameObject("BilliardsBoard");SceneManager.MoveGameObjectToScene(board,scene);MResLoadHandle view=null;
            try
            {
                simulation.StartSession(bridge);simulation.BeginRound();var c=simulation.Context;
                simulation.ApplyGlobalAttribute(991,GameAttribute.BALL_GRAVITY_MOD,AttributeOperation.Set,0);
                var definition=c.Balance.Devices.Single(x=>x.Id==9031);CampaignStateUtility.CreateDevice(c,definition,definition.Price);
                int id=c.Campaign.NextDeviceId;MorePlace(c,id,true);board.transform.SetPositionAndRotation(new Vector3(0,3,0),Quaternion.Euler(60,127,0));
                view=MResDeviceView.Create(simulation.GetOwnedDevices().Single(),board.transform,c.DeviceScripts.Get(id,definition.Id));view.Root.SetActive(true);Physics.SyncTransforms();
                Check(view.Root.GetComponentsInChildren<Collider>().All(x=>x.isTrigger),"billiards package has only admission sensors");
                var spawn=board.transform.TransformPoint(new Vector3(0,.15f,.85f));
                void Step(){simulation.BeforePhysics(dt,bridge,spawn,Quaternion.identity);bridge.Simulate(dt);simulation.AfterPhysics(bridge);}
                simulation.FireOnce();simulation.BeforePhysics(dt,bridge,spawn,Quaternion.identity);simulation.AfterPhysics(bridge);var key=c.Marbles.Keys.Single();
                var body=scene.GetRootGameObjects().Select(x=>x.GetComponent<MarbleBody>()).Single(x=>x!=null&&x.Key.Equals(key));
                body.Body.drag=0;Check(bridge.TrySetVelocity(key,board.transform.rotation*Vector3.back*3),"billiards approach");
                for(int i=0;i<200&&!c.Manager.HasComponent<HeldMotion>(c.Marbles[key]);i++)Step();
                Check(c.Manager.HasComponent<HeldMotion>(c.Marbles[key]),"real sensor swallows ball");
                Check(body.Body.isKinematic&&!body.Body.detectCollisions,"swallowed numbered ball has no collision");
                Check(body.GetComponentsInChildren<Renderer>().All(x=>x.forceRenderingOff),"swallowed numbered ball and text are hidden");
                var appearance=c.Manager.GetComponentData<MarbleAppearance>(c.Marbles[key]);int number=int.Parse(appearance.Label.ToString());
                var text=body.GetComponentInChildren<TMPro.TextMeshPro>(true);Check(text!=null&&text.text==number.ToString(),"visual number matches settlement state");
                double end=c.Manager.GetComponentData<HeldMotion>(c.Marbles[key]).ReleaseAt;
                for(int i=0;c.Round.Time+1e-7<end&&i<100;i++)Step();
                Check(!body.Body.isKinematic&&body.Body.detectCollisions,"emission restores collisions");Check(!body.GetComponent<Renderer>().forceRenderingOff,"emitted ball is visible");
                Near(6,body.Body.velocity.magnitude,"default emission speed is six");Near(19,simulation.GetOwnedDevices().Single().RemainingDurability,"actual emission uses one durability");
                Near(7,c.Manager.GetComponentData<MarbleRush>(c.Marbles[key]).Contribution,"admission and release count as one effect");
                var round=c.Round;round.RushMeter=10;c.Round=round;
                simulation.EnqueueContact(new MarbleContact{Key=key,Kind=MarbleContactKind.Score,TargetId=806,Multiplier=1});Step();
                Near(number==8?0:10+number*10,c.Round.RushMeter,"actual numbered ball uses override at settlement");
                simulation.FireOnce();Step();var fresh=c.Marbles.Keys.Single();Check(!c.Manager.HasComponent<MarbleAppearance>(c.Marbles[fresh]),"new ball has no old numbered state");
                var freshBody=scene.GetRootGameObjects().Select(x=>x.GetComponent<MarbleBody>()).Single(x=>x!=null&&x.gameObject.activeSelf&&x.Key.Equals(fresh));
                var freshText=freshBody.GetComponentInChildren<TMPro.TextMeshPro>(true);Check(freshText==null||!freshText.gameObject.activeSelf,"pool reuse clears old number presentation");
            }
            finally{view?.Dispose();simulation.Dispose();bridge.Dispose();UnityEngine.Object.DestroyImmediate(board);SceneManager.UnloadSceneAsync(scene);}
        }
    }
}
