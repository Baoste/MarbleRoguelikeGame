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
        private static void WokPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("WokPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance());
            var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact);
            var board = new GameObject("WokBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var definition = c.Balance.Devices.Single(d => d.Id == 9029); CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                int id = c.Campaign.NextDeviceId; MorePlace(c, id, true);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(simulation.GetOwnedDevices().Single(), board.transform, c.DeviceScripts.Get(id, definition.Id));
                view.Root.SetActive(true); Physics.SyncTransforms();
                var colliders = view.Root.GetComponentsInChildren<Collider>(true);
                Check(colliders.Length == 1 && colliders[0].isTrigger, "wok uses only a non-solid admission sensor");
                var bowl = view.Root.transform.Find("Bowl").GetComponent<MeshFilter>().sharedMesh;
                Check(bowl.vertexCount > 100 && view.Root.transform.Find("Grip-1") != null && view.Root.transform.Find("Grip1") != null, "packed model is an open bowl with two handles");
                var spawn = board.transform.TransformPoint(new Vector3(0, .12f, .85f));
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0;
                Check(bridge.TrySetVelocity(key, board.transform.rotation * Vector3.back * 3), "approach wok sensor");
                for (int i = 0; !c.Manager.HasComponent<HeldMotion>(c.Marbles[key]) && i < 200; i++) Step();
                Check(c.Manager.HasComponent<HeldMotion>(c.Marbles[key]), "real entry starts cooking");
                var held = c.Manager.GetComponentData<HeldMotion>(c.Marbles[key]); var frozen = body.position;
                Check(body.isKinematic && !body.detectCollisions, "held marble has no physics collisions");
                Near(17, c.Manager.GetComponentData<MarbleRush>(c.Marbles[key]).Contribution, "real entry grants exactly fifteen contribution");
                Near(20, simulation.GetOwnedDevices().Single().RemainingDurability, "real admission spends no wear");
                for (int i = 0; c.Round.Time + dt < held.ReleaseAt - 1e-7 && i < 150; i++)
                {
                    Step(); VectorNear(frozen, body.position, "held position remains fixed for the full wait");
                    Check(body.isKinematic && !body.detectCollisions, "collisions remain disabled during hold");
                }
                Step();
                Check(!body.isKinematic && body.detectCollisions && !c.Manager.HasComponent<HeldMotion>(c.Marbles[key]), "one-second completion restores physical collision mode");
                Near(held.OriginalVelocity.magnitude, body.velocity.magnitude, "throw preserves measured real entry speed");
                Check(bridge.TryGetDevicePose(id, out var pose), "wok physical pose available");
                Vector3 uphill = -BallInteractionSystem.Down(pose).normalized;
                Check(Vector3.Angle(uphill, body.velocity) <= 60.001f, "real throw uses uphill fan on rotated board");
                Near(0, Vector3.Dot(pose.Up.normalized, body.velocity), "throw stays within board plane");
                Near(0, c.BoardRush.Bonus(c), "release removes bonus immediately");
                Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "real throw costs one wear");
                VectorNear(body.velocity, c.Manager.GetComponentData<Motion3D>(c.Marbles[key]).LinearVelocity, "throw synchronizes ECS and PhysX velocity");
                Check(bridge.TryMove(key, spawn, Vector3.zero), "leave sensor before cooldown pass"); Step();
                Check(bridge.TrySetVelocity(key, board.transform.rotation * Vector3.back * 3), "pass through cooling pot");
                for (int i = 0; i < Mathf.CeilToInt(.65f / dt); i++) Step();
                Check(board.transform.InverseTransformPoint(body.position).z < -.6f, "cooldown permits physical pass-through");
                Check(!body.isKinematic && body.detectCollisions, "cooldown does not suspend incoming marble");
                Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "cooldown pass spends no wear");
                Near(0, c.BoardRush.Bonus(c), "cooldown pass grants no probability");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
