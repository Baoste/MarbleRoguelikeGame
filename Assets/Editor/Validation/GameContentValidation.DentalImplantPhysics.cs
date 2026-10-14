using System;
using System.Linq;
using System.Reflection;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void DentalPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("DentalPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); int contacts = 0;
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) contacts++; simulation.EnqueueContact(hit); });
            var board = new GameObject("DentalBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); int id = DentalFood(simulation.Context); var c = simulation.Context; MorePlace(c, id, true);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                var script = c.DeviceScripts.Get(id, 9028);
                void Rebuild()
                {
                    view?.Dispose(); view = MResDeviceView.Create(simulation.GetOwnedDevices().Single(), board.transform, script);
                    view.Root.SetActive(true); script.Invoke("Start"); Physics.SyncTransforms();
                }
                Rebuild(); Check(simulation.BeginRound(), "begin physical hole round");
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 1);
                simulation.ApplyGlobalAttribute(995, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var spawn = board.transform.TransformPoint(new Vector3(0, .07f, .8f));
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0; var incoming = board.transform.rotation * Vector3.back * 2;
                var collider = view.Root.GetComponentInChildren<MeshCollider>();
                Check(!collider.enabled, "zero capacity disables hole solid collider");
                Check(bridge.TrySetVelocity(key, incoming), "send ball through empty hole");
                for (int i = 0; i < Mathf.CeilToInt(1 / dt); i++) Step();
                Near(0, contacts, "empty hole causes no physical impact");
                Check(board.transform.InverseTransformPoint(body.position).z < -.5f, "ball physically crosses hole");
                Check(view.Root.transform.Find("Hole").GetComponent<MeshRenderer>().enabled, "hole face visible");
                BagBuild(c); Check(simulation.TryOfferDevice(DentalFood(c), id), "plant steel tooth into physical hole");
                Rebuild(); Check(simulation.BeginRound(), "start planted round");
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); key = c.Marbles.Keys.Single();
                body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0;
                collider = view.Root.GetComponentInChildren<MeshCollider>();
                Check(collider.enabled && !collider.isTrigger, "planting restores physical bounce");
                Check(view.Root.transform.Find("Steel").GetComponentsInChildren<Renderer>().All(r => r.enabled), "rebuilt view retains steel appearance");
                Check(!view.Root.transform.Find("Hole").GetComponent<MeshRenderer>().enabled, "planted view hides empty hole");
                for (int use = 1; use <= 5; use++)
                {
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "leave tooth before next impact"); Step(); int previous = contacts;
                    Check(bridge.TrySetVelocity(key, incoming), "approach planted tooth");
                    for (int i = 0; contacts == previous && i < 150; i++) Step();
                    Near(previous + 1, contacts, "physical impact enters once");
                    Near(1 + use * 2, c.Manager.GetComponentData<MarbleScore>(c.Marbles[key]).ScoreMultiplier, "real steel impact adds two points");
                    Check(Vector3.Dot(body.velocity, incoming) < 0, "original ball bounces normally including last use");
                }
                Check(!collider.enabled, "final use disables collider immediately");
                Check(view.Root.transform.Find("Hole").GetComponent<MeshRenderer>().enabled &&
                    view.Root.transform.Find("Steel").GetComponentsInChildren<Renderer>().All(r => !r.enabled), "final use switches visual to hole");
                Rebuild(); Check(view.Root.transform.Find("Hole").GetComponent<MeshRenderer>().enabled, "hole survives view replacement");
                Check(bridge.TryMove(key, spawn, incoming), "send ball back through exhausted hole");
                int beforePass = contacts;
                for (int i = 0; i < Mathf.CeilToInt(1 / dt); i++) Step();
                Near(beforePass, contacts, "exhausted hole no longer collides");
                Check(board.transform.InverseTransformPoint(body.position).z < -.5f, "ball passes exhausted hole");
                BagBuild(c);
                foreach (string node in new[] { "Steel", "Ceramic", "Diamond" })
                {
                    Check(simulation.TryOfferDevice(DentalFood(c), id), "successive physical model upgrade"); Rebuild();
                    Check(view.Root.transform.Find(node).GetComponentsInChildren<Renderer>().All(r => r.enabled), "each stage restores selected appearance after view rebuild");
                }
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
        private static void DentalOfferUi()
        {
            using (var f = new Fixture())
            {
                var tooth = AddDental(f); BagBuild(f.C);
                var root = new GameObject("Dental UI board"); var control = new GameObject("Dental controller"); var cameraObject = new GameObject("Dental camera");
                var cardObject = new GameObject("Dental inventory card", typeof(RectTransform), typeof(UnityEngine.UI.Button));
                var eventsObject = new GameObject("Dental events", typeof(EventSystem));
                var controller = control.AddComponent<MarbleGameController>(); controller.enabled = false;
                var field = typeof(MarbleGameController).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic); field.SetValue(controller, f.Simulation);
                controller.PhysicsRoot = root.transform;
                var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5;
                camera.transform.SetPositionAndRotation(new Vector3(0, 10, 0), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                var placement = control.AddComponent<DevicePlacementUI>(); placement.Controller = controller;
                placement.BoardRoot = root.transform; placement.PlacementPlane = root.transform; placement.ViewCamera = camera;
                var card = cardObject.AddComponent<OwnedDeviceButtonUI>();
                try
                {
                    for (int stage = 1; stage <= 4; stage++)
                    {
                        int source = DentalFood(f.C); card.Bind(f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == source), true, placement.BeginPlacement, placement);
                        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = camera.WorldToScreenPoint(Vector3.zero) };
                        card.OnPointerDown(data); card.OnBeginDrag(data); card.OnDrag(data); card.OnEndDrag(data);
                        Check(CampaignStateUtility.FindDevice(f.C, source, out _) == (stage == 4), "real inventory drag consumes only accepted upgrades");
                        var grown = f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == tooth.InstanceId);
                        Near(stage == 1 ? 5 : stage == 2 ? 8 : 20, grown.Durability, "inventory snapshot uses stage capacity");
                        if (stage < 4) Check(placement.LastFeedback.Contains(stage == 1 ? "不锈钢牙" : stage == 2 ? "陶瓷牙" : "金刚钻牙"), "drag feedback shows committed stage");
                    }
                }
                finally
                {
                    field.SetValue(controller, null); UnityEngine.Object.DestroyImmediate(cardObject); UnityEngine.Object.DestroyImmediate(control);
                    UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(eventsObject); UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }
    }
}
