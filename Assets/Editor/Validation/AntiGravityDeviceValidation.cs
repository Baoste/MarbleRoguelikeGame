using System;
using System.Collections.Generic;
using System.Linq;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static class AntiGravityDeviceValidation
    {
        private static int assertions;
        public static void Validate()
        {
            assertions = 0;
            foreach (float dt in new[] { .1f, .02f })
                using (var r = new Rig(dt))
                {
                    var key = r.Launch(); r.Contact(key, 3);
                    var body = r.Body(key);
                    float radius = body.GetComponent<MarbleBody>().Radius;
                    Near(r.Pose.Position + r.Pose.Up * (radius + .005f) - r.Pose.Down() * (r.Pose.Radius + radius + .02f),
                        body.position, "exit above device on inclined board");
                    var initial = -r.Pose.Down() * 3;
                    Near(initial, body.velocity, "launch preserves speed and chooses uphill direction");
                    Near(new Vector3(1, 2, 3), body.angularVelocity, "spin preserved");
                    Check(r.Active(key) && r.Gravity(key) == -1, "Lua applies reverse gravity immediately");
                    Check(r.Simulation.GetOwnedDevices().Single().RemainingDurability == 19, "one use for launch plus gravity");
                    int count = Mathf.RoundToInt(2 / dt);
                    for (int i = 0; i < count - 1; i++) r.Step();
                    Check(r.Active(key), "effect survives until full duration");
                    Near(initial - Physics.gravity * (dt * (count - 1)), body.velocity, "real upward acceleration during effect", .01f);
                    r.Step();
                    Check(!r.Active(key) && r.Gravity(key) == 1, "restored at two seconds");
                    Near(initial - Physics.gravity * 2, body.velocity, "last step still receives reverse gravity", .01f);
                    var last = body.velocity; r.Step();
                    Near(last + Physics.gravity * dt, body.velocity, "ordinary gravity resumes next step", .01f);
                }
            foreach (uint otherId in new uint[] { 113, 9001, 9004, 9006 })
                using (var r = new Rig())
                {
                    var key = r.Launch(); r.Contact(key, 3);
                    var other = r.Add(otherId, new Vector3(5, 0, 0));
                    r.Hit(key, other);
                    if (otherId == 9006)
                    {
                        var effect = r.C.Manager.GetComponentData<TimedGravity>(r.Ball(key));
                        Check(effect.SourceId == other.InstanceId && effect.EndsAt > r.C.Round.Time,
                            "another instance ends previous gravity then starts a new effect");
                    }
                    else Check(!r.Active(key) && r.Gravity(key) == 1, "other device resets before its Lua, including field-only devices");
                    if (otherId == 9001) Check(r.C.Manager.HasComponent<HeldMotion>(r.Ball(key)), "sticky still applies its hold after gravity reset");
                }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Contact(key, 2);
                var deadline = r.C.Manager.GetComponentData<TimedGravity>(r.Ball(key)).EndsAt;
                r.Hit(key, r.Source);
                Check(r.Active(key) && r.C.Manager.GetComponentData<TimedGravity>(r.Ball(key)).EndsAt == deadline,
                    "same source does not cancel or refresh once-per-ball effect");
                r.HitPin(key);
                Check(r.Active(key), "ordinary pins do not count as other devices");
                Check(r.Simulation.ApplyAttribute(key, 987, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 2), "change underlying gravity during effect");
                r.Step(); Check(r.Gravity(key) == -2, "temporary multiplier composes with current attributes");
                r.Hit(key, r.Add(113, Vector3.right * 5));
                Check(!r.Active(key) && r.Gravity(key) == 2, "restore current gravity, not a stale snapshot");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); var api = r.Api(key); var m = api.Motion;
                var position = r.Body(key).position; var velocity = r.Body(key).velocity;
                Check(!m.TrySetGravityScale(float.NaN, 2, true) && !m.TrySetGravityScale(float.PositiveInfinity, 2, true) &&
                    !m.TrySetGravityScale(11, 2, true) && !m.TrySetGravityScale(-11, 2, true), "invalid scales rejected");
                foreach (float seconds in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 61f })
                    Check(!m.TrySetGravityScale(-1, seconds, true), "invalid duration rejected");
                Check(!r.Active(key), "invalid calls leave no effect");
                Check(m.TrySetGravityScale(0, 1, false), "zero gravity is reusable independently of launch");
                Near(position, r.Body(key).position, "gravity API does not move");
                Near(velocity, r.Body(key).velocity, "gravity API does not launch");
                Check(m.TrySetGravityScale(.5f, 3, false) && r.Gravity(key) == .5f, "new scale replaces previous effect");
                api.Close();
                bool closed = false; try { m.TryClearGravityScale(); } catch (InvalidOperationException) { closed = true; }
                Check(closed, "gravity API cannot escape callback lifetime");
                r.Hit(key, r.Add(113, Vector3.right * 5));
                Check(r.Active(key) && r.Gravity(key) == .5f, "contact reset is optional");
                api = r.Api(key);
                Check(api.Motion.TryClearGravityScale() && !api.Motion.TryClearGravityScale() && r.Gravity(key) == 1, "explicit clear only removes temporary gravity");
                api.Close();
            }
            using (var r = new Rig())
            {
                r.C.Balance.Devices.Single(d => d.Id == 9006).Durability = 1;
                var key = r.Launch(); r.Contact(key, 2);
                Check(r.Active(key) && r.Simulation.GetOwnedDevices().Single().Exhausted, "final durability use completes");
                for (int i = 0; i < 19; i++) r.Step();
                Check(r.Active(key), "exhaustion does not cancel accepted gravity early");
                r.Step(); Check(!r.Active(key), "exhausted source still expires gravity normally");
            }
            foreach (bool remove in new[] { false, true })
                using (var r = new Rig())
                {
                    var key = r.Launch(); r.Contact(key, 2);
                    if (remove) r.C.Manager.DestroyEntity(r.Device);
                    else { var d = r.C.Manager.GetComponentData<OwnedDeviceData>(r.Device); d.Placed = false; r.C.Manager.SetComponentData(r.Device, d); }
                    r.Step(); Check(!r.Active(key) && r.Gravity(key) == 1, "source removed or recalled restores gravity");
                }
            using (var r = new Rig())
            {
                var first = r.Launch(); r.Contact(first, 2); var pooled = r.Body(first);
                var second = r.Launch();
                Check(r.Active(first) && !r.Active(second) && r.Gravity(second) == 1, "per-ball isolation");
                var f = r.C.Manager.GetComponentData<BallFeatures>(r.Ball(first)); f.BirthTick = r.C.Round.Tick - 599;
                r.C.Manager.SetComponentData(r.Ball(first), f); r.Step();
                Check(!r.C.Marbles.ContainsKey(first), "lifetime removes ball with active effect");
                var next = r.Launch();
                Check(r.Body(next) == pooled && !r.Active(next) && r.Gravity(next) == 1, "pool reuse has normal gravity");
                r.Contact(next, 2); r.Simulation.StartSession(r.Bridge);
                Check(r.C.Marbles.Count == 0 && r.Bridge.ActiveBodyCount == 0, "session reset clears timed effects");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); float radius = r.Body(key).GetComponent<MarbleBody>().Radius;
                var obstacle = new GameObject("BlockedAntiGravityExit"); SceneManager.MoveGameObjectToScene(obstacle, r.Scene);
                obstacle.transform.position = r.Pose.Position + r.Pose.Up * (radius + .005f) - r.Pose.Down() * (r.Pose.Radius + radius + .02f);
                obstacle.AddComponent<BoxCollider>().size = Vector3.one * .3f; Physics.SyncTransforms();
                try { var before = r.Body(key).position; r.Contact(key, 2);
                    Check(!r.Active(key) && r.Gravity(key) == 1, "blocked launch does not grant gravity effect");
                    Near(before, r.Body(key).position, "blocked launch does not embed ball in geometry"); }
                finally { UnityEngine.Object.DestroyImmediate(obstacle); }
            }
            using (var r = new Rig())
            {
                r.Simulation.ApplyGlobalAttribute(988, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var key = r.Launch(r.Board.transform.TransformPoint(new Vector3(0, .08f, .8f)));
                r.Bridge.TrySetVelocity(key, r.Board.transform.rotation * Vector3.back * 2);
                for (int i = 0; i < 8 && !r.Active(key); i++) r.Step();
                Check(r.Active(key), "real cylinder trigger loads packaged Lua and launches");
                Near(-r.Pose.Down() * 2, r.Body(key).velocity, "physical entry preserves speed");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); var api = r.Api(key);
                Check(api.Motion.TrySetGravityScale(-1, 2, true) && api.Motion.TryHold(.3f), "gravity composes with independent hold"); api.Close();
                for (int i = 0; i < 3; i++) r.Step();
                Check(r.Active(key) && !r.C.Manager.HasComponent<HeldMotion>(r.Ball(key)), "hold completes without discarding timed gravity");
                for (int i = 0; i < 17; i++) r.Step();
                Check(!r.Active(key), "gravity timer includes time spent held");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Contact(key, 2);
                var field = r.Add(9004, new Vector3(5, 0, 0));
                Check(!field.Script.HasFunction("OnContact"), "field only has a force callback");
                Check(r.Bridge.TryMove(key, field.GetComponentInChildren<BoxCollider>().bounds.center, Vector3.zero), "move into actual field sensor");
                r.Step();
                Check(!r.Active(key) && r.Gravity(key) == 1, "real trigger contact resets even without OnContact handler");
            }
            Debug.Log("ANTI_GRAVITY_VALIDATION_PASSED: " + assertions + " assertions; packaged Lua, real gravity, duration, contacts and lifecycle.");
        }

        private static Vector3 Down(this DevicePose pose) => BallInteractionSystem.Down(pose);
        private static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new Exception("AntiGravity: " + message); }
        private static void Near(Vector3 expected, Vector3 actual, string message, float tolerance = .0001f) =>
            Check((expected - actual).magnitude < tolerance, message + ": " + expected + " vs " + actual);

        private sealed class Rig : IDisposable
        {
            internal readonly Scene Scene;
            internal readonly GameObject Board;
            internal readonly MarbleSimulation Simulation;
            internal readonly MarblePhysicsBridge Bridge;
            internal readonly Entity Device;
            internal readonly MResDeviceBody Source;
            internal SimulationContext C => Simulation.Context;
            internal DevicePose Pose { get { Bridge.TryGetDevicePose(Source.InstanceId, out var pose); return pose; } }
            private readonly List<MResLoadHandle> views = new List<MResLoadHandle>();
            private readonly float dt;
            private int launches;
            internal Rig(float dt = .1f)
            {
                this.dt = dt;
                Scene = SceneManager.CreateScene("AntiGravityValidation_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var balance = GameBalanceLoader.Load();
                foreach (var d in balance.Devices) d.StartingCount = 0;
                balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 1000;
                balance.Marble.FireIntervalSeconds = balance.Marble.MaxFireIntervalSeconds = .01f; // Isolate scripted launches from the authored cooldown.
                balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue = 60;
                balance.Content.Definition(GameAttribute.BALL_MAX_SPEED).DefaultValue = 100;
                Simulation = new MarbleSimulation(balance);
                Bridge = new MarblePhysicsBridge(Scene, null, null, Simulation.EnqueueContact);
                Simulation.StartSession(Bridge); Simulation.BeginRound();
                Board = new GameObject("AntiGravityBoard"); SceneManager.MoveGameObjectToScene(Board, Scene);
                Board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 47, 0));
                Source = Add(9006, Vector3.zero);
                CampaignStateUtility.FindDevice(C, Source.InstanceId, out Device);
                Check(Source.GetComponentInChildren<MeshCollider>().isTrigger, "package has a cylinder trigger");
            }
            internal MResDeviceBody Add(uint id, Vector3 position)
            {
                var definition = C.Balance.Devices.Single(d => d.Id == id);
                CampaignStateUtility.CreateDevice(C, definition, definition.Price);
                var owned = Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
                CampaignStateUtility.FindDevice(C, owned.InstanceId, out var entity);
                var data = C.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; C.Manager.SetComponentData(entity, data);
                var view = MResDeviceView.Create(owned, Board.transform, C.DeviceScripts.Get(owned.InstanceId, definition.Id));
                views.Add(view); view.Root.transform.localPosition = position; view.Root.SetActive(true); Physics.SyncTransforms();
                return view.Root.GetComponent<MResDeviceBody>();
            }
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            internal bool Active(MarbleKey key) => C.Manager.HasComponent<TimedGravity>(Ball(key));
            internal float Gravity(MarbleKey key) => Body(key).GetComponent<MarbleBody>().Settings.Gravity;
            internal Rigidbody Body(MarbleKey key) => Scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
            internal MarbleKey Launch(Vector3? position = null)
            {
                Simulation.FireOnce();
                Simulation.BeforePhysics(dt, Bridge, position ?? new Vector3(3 + launches++ * .6f, 4, 0), Quaternion.identity);
                Simulation.AfterPhysics(Bridge);
                var key = C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                Body(key).drag = Body(key).angularDrag = 0;
                return key;
            }
            internal DeviceContactContext Api(MarbleKey key) => new DeviceContactContext(C, Ball(key), Device, Source.Contact(key, Body(key).position), Pose);
            internal void Contact(MarbleKey key, float speed)
            {
                Simulation.BeforePhysics(dt, Bridge, Vector3.up * 10, Quaternion.identity);
                Bridge.TrySetVelocity(key, new Vector3(3, 0, 4).normalized * speed); Body(key).angularVelocity = new Vector3(1, 2, 3);
                Simulation.EnqueueContact(Source.Contact(key, Body(key).position)); Simulation.AfterPhysics(Bridge);
            }
            internal void Hit(MarbleKey key, MResDeviceBody other)
            {
                Simulation.BeforePhysics(dt, Bridge, Vector3.up * 10, Quaternion.identity);
                Simulation.EnqueueContact(other.Contact(key, Body(key).position)); Simulation.AfterPhysics(Bridge);
            }
            internal void HitPin(MarbleKey key)
            {
                Simulation.BeforePhysics(dt, Bridge, Vector3.up * 10, Quaternion.identity);
                Simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Pin, TargetId = 13, Multiplier = 1 });
                Simulation.AfterPhysics(Bridge);
            }
            internal void Step()
            { Simulation.BeforePhysics(dt, Bridge, Vector3.up * 10, Quaternion.identity); Bridge.Simulate(dt); Simulation.AfterPhysics(Bridge); }
            public void Dispose()
            {
                foreach (var view in views) view.Dispose();
                Simulation.Dispose(); Bridge.Dispose(); UnityEngine.Object.DestroyImmediate(Board); SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }
}
