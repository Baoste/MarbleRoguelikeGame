using System;
using System.Linq;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void VectorNear(Vector3 expected, Vector3 actual, string message)
        { Check((expected - actual).magnitude < .0001f, message + " expected " + expected + ", actual " + actual); }

        private static void DeviceLuaApi()
        {
            // Execute the shipped package, with unrelated entry velocity and exit orientation.
            foreach (float speed in new[] { 0f, .03f, 5f })
            foreach (float yaw in new[] { 0f, 90f, 213f })
            using (var f = new Fixture())
            {
                var entry = f.Add(DeviceKind.Portal);
                int exitId = f.Physics.Poses.Keys.Single(id => id != entry.InstanceId);
                var rotation = Quaternion.Euler(61, yaw, 13);
                var exit = f.Physics.Poses[exitId];
                exit.Forward = rotation * Vector3.back; exit.Up = rotation * Vector3.up;
                f.Physics.Poses[exitId] = exit;
                var key = f.Launch(); var ball = f.Ball(key);
                var before = f.Physics.States[key];
                before.LinearVelocity = new Vector3(3, -4, 0).normalized * speed;
                before.AngularVelocity = new Vector3(2, 3, 4);
                f.Physics.States[key] = before;
                f.Step(f.Hit(key, entry));
                var after = f.Physics.States[key];
                Near(speed, after.LinearVelocity.magnitude, "Lua portal preserves speed including zero and sub-0.1");
                VectorNear(exit.Forward * speed, after.LinearVelocity, "Lua portal uses rotated exit facing");
                VectorNear(after.LinearVelocity, f.C.Manager.GetComponentData<Motion3D>(ball).LinearVelocity, "portal synchronizes ECS velocity");
                VectorNear(before.AngularVelocity, f.C.Manager.GetComponentData<Motion3D>(ball).AngularVelocity, "portal preserves spin");
                float radius = (float)f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_RADIUS) * AttributeRuntime.RadiusToWorld;
                VectorNear(exit.Position + exit.Up * (radius + .005f) + exit.Forward * (exit.Radius + radius + .02f),
                    after.Position, "portal clears the exit and board surface");
                f.Step(f.Hit(key, entry));
                Check(f.Physics.Moves == 1, "Lua transport lock prevents immediate second teleport");
            }
            using (var f = new Fixture())
            {
                var entry = f.Add(DeviceKind.Portal); var key = f.Launch(); var ball = f.Ball(key);
                int exitId = f.Physics.Poses.Keys.Single(id => id != entry.InstanceId);
                var exit = f.Physics.Poses[exitId];
                exit.Up = Quaternion.Euler(65, 0, 0) * Vector3.up;
                f.Physics.Poses[exitId] = exit;
                float speed = f.Physics.States[key].LinearVelocity.magnitude;
                var features = f.C.Manager.GetComponentData<BallFeatures>(ball); features.ExitDown = true;
                f.C.Manager.SetComponentData(ball, features);
                f.Step(f.Hit(key, entry));
                VectorNear(Vector3.ProjectOnPlane(Vector3.down, exit.Up).normalized * speed,
                    f.Physics.States[key].LinearVelocity, "Lua composes downhill medicine with motion API");
                Check(!f.C.Manager.GetComponentData<BallFeatures>(ball).ExitDown, "successful teleport consumes downhill medicine once");
            }
            using (var f = new Fixture())
            {
                var entry = f.Add(DeviceKind.Portal); var key = f.Launch(); var ball = f.Ball(key);
                var before = f.Physics.States[key];
                var features = f.C.Manager.GetComponentData<BallFeatures>(ball); features.ExitDown = true;
                f.C.Manager.SetComponentData(ball, features);
                f.Physics.BlockMoves = true;
                f.Step(f.Hit(key, entry));
                VectorNear(before.Position, f.Physics.States[key].Position, "blocked portal leaves body position");
                VectorNear(before.LinearVelocity, f.C.Manager.GetComponentData<Motion3D>(ball).LinearVelocity, "blocked portal leaves ECS velocity");
                features = f.C.Manager.GetComponentData<BallFeatures>(ball);
                Check(features.ExitDown && features.PortalLockedUntil == 0, "failed teleport preserves medicine and transport lock");
            }
            using (var f = new Fixture())
            {
                var entry = f.Add(DeviceKind.Portal); var key = f.Launch();
                int exitId = f.Physics.Poses.Keys.Single(id => id != entry.InstanceId);
                CampaignStateUtility.FindDevice(f.C, exitId, out var exitEntity);
                var exit = f.C.Manager.GetComponentData<OwnedDeviceData>(exitEntity); exit.Placed = false;
                f.C.Manager.SetComponentData(exitEntity, exit);
                f.Step(f.Hit(key, entry));
                Check(f.Physics.Moves == 0 && f.C.Manager.GetBuffer<DeviceVisit>(f.Ball(key)).Length == 0,
                    "unplaced partner is absent and does not consume trigger");
            }
            using (var f = new Fixture())
            {
                var entry = f.Add(DeviceKind.Portal); var key = f.Launch(); var ball = f.Ball(key);
                CampaignStateUtility.FindDevice(f.C, entry.InstanceId, out var entity);
                var api = new DeviceContactContext(f.C, ball, entity, f.Hit(key, entry), f.Physics.Poses[entry.InstanceId]);
                var motion = api.Motion; var exit = motion.FindPartner();
                try
                {
                    Check(exit != null && motion.FindDevice(exit.InstanceId) != null && motion.FindDevice(-1) == null, "motion endpoint lookup");
                    VectorNear(Vector3.right, motion.Source.Rotate(Vector3.forward, 90), "Lua direction rotation in degrees");
                    var before = motion.Position;
                    Check(!motion.TryLaunch(exit, Vector3.zero, 2) && !motion.TryLaunch(exit, Vector3.up, -1) &&
                        !motion.TryLaunch(exit, Vector3.up, float.NaN) && !motion.TryTeleport(null), "invalid launch rejected");
                    Check(!motion.TrySetVelocity(new Vector3(float.PositiveInfinity, 0, 0)) &&
                        !motion.TryScaleSpeed(-1) && !motion.TryMove(new Vector3(float.NaN, 0, 0), Vector3.zero), "invalid motion rejected");
                    VectorNear(before, motion.Position, "invalid API calls leave position unchanged");
                    f.Physics.BlockMoves = true;
                    Check(motion.TrySetVelocity(new Vector3(3, 0, 4)) && motion.TryScaleSpeed(.5f), "velocity changes need no teleport clearance");
                    Near(2.5, motion.Speed, "scaled speed is visible immediately");
                    VectorNear(before, motion.Position, "velocity change leaves position unchanged");
                    VectorNear(motion.Velocity, f.Physics.States[key].LinearVelocity, "velocity change synchronizes body");
                    double cap = f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_MAX_SPEED);
                    Check(motion.TrySetVelocity(Vector3.right * (float)(cap * 2)), "engine accepts capped velocity");
                    Near(cap, motion.Speed, "motion API observes global speed limit");
                    f.Physics.BlockMoves = false;
                    Check(motion.TryMove(new Vector3(0, 3, 0), Vector3.left), "arbitrary motion command");
                    VectorNear(new Vector3(0, 3, 0), f.Physics.States[key].Position, "arbitrary move synchronizes body");
                    var another = new DeviceContactContext(f.C, ball, entity, f.Hit(key, entry), f.Physics.Poses[entry.InstanceId]);
                    try { Check(!another.Motion.TryTeleport(exit), "endpoint cannot escape originating callback"); }
                    finally { another.Close(); }
                }
                finally { api.Close(); }
                bool expired = false;
                try { motion.TryTeleport(exit); } catch (InvalidOperationException) { expired = true; }
                Check(expired, "motion API rejects use after callback");
                expired = false;
                try { _ = exit.Forward; } catch (InvalidOperationException) { expired = true; }
                Check(expired, "endpoint rejects use after callback");
            }
            PortalWithPhysX(false);
            PortalWithPhysX(true);
        }

        private static void PortalWithPhysX(bool obstructed)
        {
            var scene = SceneManager.CreateScene("LuaPortalPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var bridge = new MarblePhysicsBridge(scene, null, null, _ => { });
            var simulation = new MarbleSimulation(Balance());
            GameObject obstacle = null;
            var objects = new System.Collections.Generic.List<GameObject>();
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound();
                var c = simulation.Context;
                var definition = c.Balance.Devices.Single(x => x.Kind == DeviceKind.Portal);
                CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                var pair = simulation.GetOwnedDevices().OrderBy(x => x.InstanceId).ToArray();
                foreach (var owned in pair)
                {
                    CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                    var d = c.Manager.GetComponentData<OwnedDeviceData>(entity); d.Placed = true; c.Manager.SetComponentData(entity, d);
                    var go = new GameObject("LuaPortalEndpoint"); objects.Add(go);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.SetPositionAndRotation(new Vector3(owned.InstanceId * 3, 3, 0), Quaternion.Euler(61, 127, 19));
                    go.AddComponent<MResDeviceBody>().Initialize(owned.InstanceId, definition, c.DeviceScripts.Get(owned.InstanceId, definition.Id));
                }
                bridge.TryGetDevicePose(pair[1].InstanceId, out var exit);
                VectorNear(objects[1].transform.rotation * Vector3.back, exit.Forward, "engine exit facing is local negative Z");
                simulation.FireOnce();
                simulation.BeforePhysics(.1f, bridge, new Vector3(0, 3, 0), Quaternion.identity);
                var key = c.Marbles.Keys.Single(); var ball = c.Marbles[key];
                Check(bridge.TrySetVelocity(key, new Vector3(3, 0, 4)), "real physics velocity API");
                bridge.TryGetState(key, out var before);
                if (obstructed)
                {
                    float radius = (float)simulation.GetMarbleAttribute(key, GameAttribute.BALL_RADIUS) * AttributeRuntime.RadiusToWorld;
                    obstacle = new GameObject("BlockedExit");
                    SceneManager.MoveGameObjectToScene(obstacle, scene);
                    obstacle.transform.position = exit.Position + exit.Up * (radius + .005f) + exit.Forward * (exit.Radius + radius + .02f);
                    obstacle.AddComponent<BoxCollider>().size = Vector3.one;
                    Physics.SyncTransforms();
                }
                simulation.EnqueueContact(objects[0].GetComponent<MResDeviceBody>().Contact(key, before.Position));
                simulation.AfterPhysics(bridge);
                bridge.TryGetState(key, out var after);
                VectorNear(obstructed ? before.LinearVelocity : exit.Forward * 5, after.LinearVelocity, "Lua teleport reaches real PhysX velocity");
                VectorNear(after.Position, c.Manager.GetComponentData<Transform3D>(ball).Position, "real teleport position matches ECS");
                if (obstructed) VectorNear(before.Position, after.Position, "solid obstacle rejects real teleport");
                else Check(Vector3.Distance(before.Position, after.Position) > 1, "real portal relocates body");
                Check((c.Manager.GetComponentData<BallFeatures>(ball).PortalLockedUntil > c.Round.Time) == !obstructed,
                    "real teleport result controls Lua lock");
            }
            finally
            {
                if (obstacle != null) UnityEngine.Object.DestroyImmediate(obstacle);
                foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go);
                simulation.Dispose(); bridge.Dispose(); SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
