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
        private static void CoolingTeaPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("CoolingTeaPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); int impacts = 0;
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) impacts++; simulation.EnqueueContact(hit); });
            var board = new GameObject("CoolingTeaBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, .8);
                simulation.ApplyGlobalAttribute(995, GameAttribute.BALL_FRICTION, AttributeOperation.Set, .4);
                var definition = c.Balance.Devices.Single(d => d.Id == 9030); CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                int id = c.Campaign.NextDeviceId; MorePlace(c, id, true);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(simulation.GetOwnedDevices().Single(), board.transform, c.DeviceScripts.Get(id, definition.Id));
                view.Root.SetActive(true); Physics.SyncTransforms();
                var colliders = view.Root.GetComponentsInChildren<Collider>(true).Where(c => c.name != "PlacementVolume").ToArray();
                Check(colliders.Length == 1 && !colliders[0].isTrigger, "teapot has solid collision");
                Check(view.Root.transform.Find("Spout") != null && view.Root.transform.Find("Handle") != null && view.Root.transform.Find("Lid") != null, "packed teapot includes spout, handle and lid");
                var spawn = board.transform.TransformPoint(new Vector3(0, .18f, .85f));
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0; var incoming = board.transform.rotation * Vector3.back * 3;
                for (int pass = 0; pass < 2; pass++)
                {
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "leave previous teapot collision"); Step();
                    Check(bridge.TrySetVelocity(key, incoming), "approach teapot body"); int before = impacts; Vector3 solved = default;
                    for (int i = 0; impacts == before && i < 200; i++)
                    {
                        simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); solved = body.velocity; simulation.AfterPhysics(bridge);
                    }
                    Near(before + 1, impacts, "physical impact occurs once per approach");
                    Near(.48, simulation.GetMarbleAttribute(key, GameAttribute.BALL_FRICTION), "first impact converts friction; repeated impact is neutral");
                    Near(.72, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BOUNCE), "first impact converts bounce once");
                    Near(5, c.Manager.GetComponentData<MarbleRush>(c.Marbles[key]).DurationBonus, "physical ball retains one deferred Rush contribution");
                    Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "physical repeated impact does not spend again");
                    VectorNear(solved, body.velocity, "attribute effect does not rewrite already solved collision velocity");
                    Check(Vector3.Dot(body.velocity, incoming) < 0, "ball bounces off solid teapot");
                    Step(); var material = body.GetComponent<Collider>().sharedMaterial;
                    Near(.48, material.dynamicFriction, "next physical step receives new dynamic friction");
                    Near(.48, material.staticFriction, "next physical step receives new static friction");
                    Near(.72, material.bounciness, "next physical step receives new elasticity");
                }
                Near(0, c.Round.RushRemaining, "physical collision itself does not activate Rush");
                var round = c.Round; round.RushMeter = 665; c.Round = round;
                simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 720, Multiplier = 1 }); Step();
                Near(11, c.Round.RushRemaining, "physical treated ball fills meter and adds five seconds");
                Check(!c.Marbles.ContainsKey(key), "settlement consumes ball once");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
