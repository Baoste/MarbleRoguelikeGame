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
        private static void StarLampPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("StarLampPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); int contacts = 0;
            var bridge = new MarblePhysicsBridge(scene, null, null, contact => { contacts++; simulation.EnqueueContact(contact); });
            var board = new GameObject("StarLampBoard"); SceneManager.MoveGameObjectToScene(board, scene);
            var views = new MResLoadHandle[3];
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_MAX_SPEED, AttributeOperation.Set, 100);
                var def = c.Balance.Devices.Single(d => d.Id == 9027); def.Durability = 1;
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(63, 121, 0));
                var ids = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    CampaignStateUtility.CreateDevice(c, def, def.Price); ids[i] = c.Campaign.NextDeviceId;
                    MorePlace(c, ids[i], true); var owned = simulation.GetOwnedDevices().Single(x => x.InstanceId == ids[i]);
                    views[i] = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(ids[i], def.Id));
                    views[i].Root.transform.localPosition = Vector3.right * (i == 2 ? 0 : (i == 0 ? -2 : 2));
                    views[i].Root.SetActive(true);
                    if (i < 2) LampSet(c, ids[i], 1);
                }
                Physics.SyncTransforms();
                var root = views[2].Root;
                var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => c.name != "PlacementVolume").ToArray();
                Check(colliders.Length == 1 && colliders.All(x => x.isTrigger), "lamp contains only a pass-through sensor");
                var mesh = root.transform.Find("Frame").GetComponent<MeshFilter>().sharedMesh;
                Near(5, mesh.vertices.Where(v => Mathf.Abs(v.y - 1) < .001f && Mathf.Abs(v.x * v.x + v.z * v.z - 1) < .001f).Distinct().Count(), "packed mesh has five outer tips");
                var lit = root.transform.Find("Lit").GetComponent<MeshRenderer>(); var unlit = root.transform.Find("Unlit").GetComponent<MeshRenderer>();
                Check(!lit.enabled && unlit.enabled, "package starts with unlit face");
                Check(lit.sharedMaterial.IsKeywordEnabled("_EMISSION") && lit.sharedMaterial.GetColor("_EmissionColor").r > 1, "lit face has emissive material");
                var spawn = board.transform.TransformPoint(new Vector3(0, .12f, .85f));
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                var key = c.Marbles.Keys.Single();
                Check(bridge.TryMove(key, board.transform.TransformPoint(new Vector3(0, .12f, 0)), Vector3.zero), "enter lamp region");
                for (int i = 0; i < 30; i++) Step();
                Near(1, contacts, "stationary stay toggles once, not every frame");
                Check(simulation.TryGetMarble(key, out var activeBall), "physical ball snapshot exists"); Near(4, activeBall.ScoreMultiplier, "all three lit lamps create one shared board bonus");
                double end = LampEnd(c, ids[2]);
                Check(lit.enabled && !unlit.enabled, "Lua displays on face");
                for (int pass = 1; pass <= 3; pass++)
                {
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "leave lamp before reentry"); Step();
                    float speed = pass == 2 ? 100 : 4; var velocity = board.transform.rotation * Vector3.back * speed;
                    Check(bridge.TrySetVelocity(key, velocity), "approach sensor");
                    for (int i = 0; i < 130; i++)
                    {
                        Step(); bridge.TryGetState(key, out var state);
                        if (board.transform.InverseTransformPoint(state.Position).z < -.85f) break;
                    }
                    Check(bridge.TryGetState(key, out var output), "original ball survives lamp");
                    Check(board.transform.InverseTransformPoint(output.Position).z < -.85f, "ball crosses entire lamp with no bounce");
                    VectorNear(velocity.normalized, output.LinearVelocity.normalized, "sensor does not deflect velocity");
                    Check(output.LinearVelocity.magnitude > speed * .9f && output.LinearVelocity.magnitude <= speed + .001f, "only existing damping changes speed");
                    Near(pass + 1, contacts, "reentry and fast sweep report once");
                    Check(simulation.TryGetMarble(key, out activeBall), "active ball survives"); Near(4, activeBall.ScoreMultiplier, "reentry neither stacks nor removes global effect");
                    Check(lit.enabled && !unlit.enabled, "all lamps remain lit during window");
                }
                foreach (var lamp in simulation.GetOwnedDevices()) Near(1, lamp.RemainingDurability, "wear retained throughout active window");
                Check(bridge.TryMove(key, spawn, Vector3.zero), "park ball while window completes");
                var script = c.DeviceScripts.Get(ids[2], def.Id);
                views[2].Dispose();
                views[2] = MResDeviceView.Create(simulation.GetOwnedDevices().Single(x => x.InstanceId == ids[2]), board.transform, script);
                views[2].Root.SetActive(true); script.Invoke("Start"); Step();
                lit = views[2].Root.transform.Find("Lit").GetComponent<MeshRenderer>();
                Check(lit.enabled, "replacement view restores active lamp appearance");
                CampaignStateUtility.FindDevice(c, ids[0], out var firstEntity);
                var query = new DeviceEventContext(c, firstEntity);
                // Query itself is independent of view, activity and remaining durability.
                Near(3, query.CountPlacedDevicesWithRoundValue(9027, "lit", 1), "all lit states remain queryable after replacement"); query.Close();
                for (int i = 0; c.Round.Time + 1e-7 < end && i < 600; i++) Step();
                foreach (var view in views)
                {
                    Check(!view.Root.transform.Find("Lit").GetComponent<MeshRenderer>().enabled && view.Root.transform.Find("Unlit").GetComponent<MeshRenderer>().enabled, "timeout extinguishes every physical lamp");
                    Check(!view.Root.GetComponent<BoxCollider>().enabled, "final durability disables each expired sensor");
                }
                foreach (var lamp in simulation.GetOwnedDevices()) Near(0, lamp.RemainingDurability, "one completed window charges every lamp once");
                Check(simulation.TryGetMarble(key, out activeBall), "parked ball survives timeout"); Near(1, activeBall.ScoreMultiplier, "timeout removes aura from existing physical ball");
                simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true }); Step();
                Near(10, c.Round.Score, "physical ball scoring after expiry has only base multiplier");
                var round = c.Round; round.Phase = (byte)RoundPhase.Build; c.Round = round;
                Check(simulation.BeginRound(), "next physical lamp round starts"); Step();
                foreach (var view in views) Check(!view.Root.transform.Find("Lit").GetComponent<MeshRenderer>().enabled && view.Root.transform.Find("Unlit").GetComponent<MeshRenderer>().enabled, "all lamp faces reset next round");
                Check(views[2].Root.GetComponent<BoxCollider>().enabled, "new round restores exhausted sensor");
            }
            finally
            {
                foreach (var view in views) view?.Dispose(); simulation.Dispose(); bridge.Dispose();
                UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
