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
    public static class GoldRushDeviceValidation
    {
        private static int assertions;
        public static void Validate()
        {
            assertions = 0;
            Outcomes();
            CapacityAndRounds();
            VisibilityAndCleanup();
            PhysicalEntry();
            Debug.Log("GOLD_RUSH_VALIDATION_PASSED: " + assertions + " assertions; ten-second transport, seeded outcomes, one-ball capacity, deferred durability, hidden hold lifecycle and real mine entrance.");
        }

        private static void Outcomes()
        {
            for (int outcome = 0; outcome < 3; outcome++)
                using (var r = new Rig(1))
                {
                    var key = r.Launch(); var body = r.Body(key); var renderer = body.GetComponent<Renderer>();
                    double coins = r.C.Campaign.Coins, value = r.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), blood = r.C.Player.Blood;
                    r.Contact(key);
                    Check(r.Held(key) && body.isKinematic && !body.detectCollisions && renderer.forceRenderingOff, "transport suspends and hides the input ball");
                    Check(r.Remaining == 1, "admission spends no durability, even on the final use");
                    Near(coins, r.C.Campaign.Coins, "no payout at admission");
                    double deadline = r.C.Manager.GetComponentData<HeldMotion>(r.Ball(key)).ReleaseAt;
                    var extra = r.Launch(); uint untouched = r.C.Round.RandomState; r.Contact(extra);
                    blood = r.C.Player.Blood;
                    Check(!r.Held(extra) && !r.Body(extra).GetComponent<Renderer>().forceRenderingOff, "busy mine leaves a second ball alone");
                    Check(r.C.Round.RandomState == untouched && r.Remaining == 1, "busy contact uses neither randomness nor durability");
                    while (r.C.Round.Time + .10000001 < deadline)
                    {
                        r.Step();
                        Check(r.Held(key) && renderer.forceRenderingOff, "transport stays hidden until the full ten seconds elapse");
                    }
                    r.Seed(outcome); uint state = r.C.Round.RandomState; MarbleRules.NextRandom(ref state);
                    r.Step();
                    Check(r.C.Round.RandomState == state, "each completed trip consumes exactly one seeded random sample");
                    Check(!r.Held(key) && r.Remaining == 0, "each outcome consumes exactly the final durability");
                    Near(coins + (outcome == 0 ? 2 : outcome == 1 ? 5 : 0), r.C.Campaign.Coins, "three outcomes award two, five or zero coins");
                    Near(blood, r.C.Player.Blood, "resolution itself neither costs nor refunds blood");
                    Near(0, r.C.Round.Score, "trip resolution never awards round score");
                    if (outcome == 0)
                    {
                        Check(r.C.Marbles.ContainsKey(key) && !renderer.forceRenderingOff && body.detectCollisions && !body.isKinematic, "returned ball becomes visible and dynamic");
                        Near(value, r.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "returned ball retains its original value");
                        VectorNear(r.Down * 5, body.velocity, "returned ball preserves entry speed and launches downhill");
                        VectorNear(Rig.Spin, body.angularVelocity, "returned ball preserves spin");
                    }
                    else Check(!r.C.Marbles.ContainsKey(key) && !renderer.forceRenderingOff, "lost or exchanged ball is consumed and restored before pooling");
                    Check(r.View.Root.GetComponentsInChildren<Collider>().All(c => !c.enabled), "resolved final use disables the sensor");
                    r.Step(); Near(coins + (outcome == 0 ? 2 : outcome == 1 ? 5 : 0), r.C.Campaign.Coins, "completion cannot pay twice");
                }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Contact(key); var entry = r.Body(key).position;
                var obstacle = new GameObject("BlockedGoldRushExit"); SceneManager.MoveGameObjectToScene(obstacle, r.Scene);
                float radius = r.Marble(key).Radius;
                obstacle.transform.position = r.Pose.Position + r.Pose.Up * (radius + .005f) + r.Down * (r.Pose.Radius + radius + .02f);
                obstacle.AddComponent<BoxCollider>().size = Vector3.one * .3f; Physics.SyncTransforms();
                try
                {
                    r.Complete(key, 0);
                    Check(!r.Held(key) && !r.Body(key).GetComponent<Renderer>().forceRenderingOff, "blocked return cannot strand a hidden ball");
                    VectorNear(entry, r.Body(key).position, "blocked return keeps its safe entry position");
                    VectorNear(r.Down * 5, r.Body(key).velocity, "blocked return falls back to downhill motion");
                    Check(r.Remaining == 19, "blocked exit fallback counts one completed effect");
                }
                finally { UnityEngine.Object.DestroyImmediate(obstacle); }
            }
        }

        private static void CapacityAndRounds()
        {
            using (var r = new Rig())
            {
                var keys = new[] { r.Launch(), r.Launch(), r.Launch() };
                r.Contact(keys);
                Check(keys.Count(r.Held) == 1 && r.Held(keys[0]), "simultaneous arrivals accept only the first deterministic ball");
                var other = r.Add(new Vector3(3, 0, 0));
                r.Step(); r.Step();
                r.Contact(new[] { keys[1] }, other.Root.GetComponent<MResDeviceBody>());
                Check(r.Held(keys[0]) && r.Held(keys[1]) && !r.Held(keys[2]), "each mine has independent capacity and deadline");
                double coins = r.C.Campaign.Coins;
                r.Complete(keys[0], 1);
                Check(r.Held(keys[1]) && r.Remaining == 19, "one mine completes without releasing the other's cargo");
                Near(coins + 5, r.C.Campaign.Coins, "independent mine pays only its own result");
                r.Contact(keys[2]); Check(r.Held(keys[2]), "mine accepts another trip after completion");
                var round = r.C.Round; round.Phase = (byte)RoundPhase.Build; r.C.Round = round;
                Check(r.Simulation.BeginRound(), "new round can start with pending trips");
                r.Tick();
                Check(r.C.Marbles.Count == 0 && r.Remaining == 20 && r.Simulation.GetDeviceStatus(r.Id).Contains("等待弹珠"), "new round clears pending trips and restores durability");
                Near(coins + 5, r.C.Campaign.Coins, "round reset never pays cancelled cargo");
                var fresh = r.Launch(); r.Contact(fresh); r.Complete(fresh, 0);
                Check(r.C.Marbles.ContainsKey(fresh) && r.Remaining == 19, "new round creates fresh transport state");
            }
            using (var r = new Rig(-1, .1))
            {
                var key = r.Launch(); double coins = r.C.Campaign.Coins;
                for (int i = 0; i < 22; i++)
                { r.Contact(key); r.Complete(key, 0); Check(r.C.Marbles.ContainsKey(key) && r.Remaining == -1, "unlimited mine supports repeated trips of the same ball"); }
                Near(coins + 44, r.C.Campaign.Coins, "every trip has a new use key and exactly one payout");
            }
        }

        private static void VisibilityAndCleanup()
        {
            using (var r = new Rig())
            {
                var key = r.Launch(); var renderer = r.Body(key).GetComponent<Renderer>();
                var api = r.Api(key);
                Check(!api.Motion.TrySetHeldVisible(false), "visibility API cannot hide a freely moving ball");
                Check(api.Motion.TryHold(.1f) && api.Motion.TrySetHeldVisible(false), "visibility composes with a plain timed hold");
                Check(api.Motion.TrySetHeldVisible(false) && renderer.forceRenderingOff, "repeated hide preserves the original state");
                Check(api.Motion.TrySetHeldVisible(true) && !renderer.forceRenderingOff && r.Held(key), "showing a ball does not release its hold");
                renderer.forceRenderingOff = true;
                Check(api.Motion.TrySetHeldVisible(false), "pre-hidden renderer can be held");
                api.Close(); bool expired = false;
                try { api.Motion.TrySetHeldVisible(true); } catch (InvalidOperationException) { expired = true; }
                Check(expired, "held visibility context expires at the callback boundary");
                r.Step(); Check(!r.Held(key) && renderer.forceRenderingOff, "completion restores a renderer that was already hidden, even without a matching Lua trip");
                renderer.forceRenderingOff = false;
                r.Contact(key);
                var other = r.Add(new Vector3(3, 0, 0));
                CampaignStateUtility.FindDevice(r.C, other.Root.GetComponent<MResDeviceBody>().InstanceId, out var otherEntity);
                r.Bridge.TryGetDevicePose(other.Root.GetComponent<MResDeviceBody>().InstanceId, out var pose);
                var wrongOwner = new DeviceContactContext(r.C, r.Ball(key), otherEntity, default, pose);
                Check(!wrongOwner.Motion.TrySetHeldVisible(true) && renderer.forceRenderingOff, "another device cannot reveal the source's cargo"); wrongOwner.Close();
            }
            for (int mode = 0; mode < 3; mode++)
                using (var r = new Rig())
                {
                    var key = r.Launch(); r.Contact(key); var body = r.Body(key); double coins = r.C.Campaign.Coins;
                    if (mode == 0) r.C.Manager.DestroyEntity(r.Device);
                    else if (mode == 1) { var d = r.C.Manager.GetComponentData<OwnedDeviceData>(r.Device); d.Placed = false; r.C.Manager.SetComponentData(r.Device, d); }
                    else r.View.Root.SetActive(false);
                    r.Step();
                    Check(!r.Held(key) && !body.isKinematic && !body.GetComponent<Renderer>().forceRenderingOff, "remove, recall or missing view restores visibility and motion");
                    VectorNear(new Vector3(3, 0, 4), body.velocity, "cancellation preserves original motion instead of producing an outcome");
                    Near(coins, r.C.Campaign.Coins, "cancelled transport never pays");
                    if (mode != 0) Check(r.Remaining == 20, "cancelled transport never spends durability");
                }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Contact(key); var body = r.Body(key); double coins = r.C.Campaign.Coins;
                var features = r.C.Manager.GetComponentData<BallFeatures>(r.Ball(key)); features.BirthTick = r.C.Round.Tick - 599;
                r.C.Manager.SetComponentData(r.Ball(key), features);
                r.Complete(key, 1);
                Check(!r.C.Marbles.ContainsKey(key) && !body.GetComponent<Renderer>().forceRenderingOff, "expiry at the deadline returns hidden cargo to the pool without an outcome");
                Near(coins, r.C.Campaign.Coins, "expiry at the deadline does not pay");
                Check(r.Remaining == 20, "expired cargo does not spend durability");
                var next = r.Launch(); Check(r.Body(next) == body && !r.Held(next) && !body.GetComponent<Renderer>().forceRenderingOff, "pool reuse starts visible and unsuspended");
                r.Contact(next); Check(r.Held(next), "expired cargo frees the mine's capacity");
                r.Simulation.StartSession(r.Bridge);
                Check(r.C.Marbles.Count == 0 && r.Bridge.ActiveBodyCount == 0 && !body.GetComponent<Renderer>().forceRenderingOff, "session reset restores hidden renderers before pooling");
            }
        }

        private static void PhysicalEntry()
        {
            using (var r = new Rig())
            {
                r.Simulation.ApplyGlobalAttribute(981, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var key = r.Launch(r.Board.transform.TransformPoint(new Vector3(0, .1f, -.9f)));
                r.Bridge.TrySetVelocity(key, r.Board.transform.rotation * Vector3.forward * 2);
                for (int i = 0; i < 10 && !r.Held(key); i++) r.Step(true);
                Check(r.Held(key) && r.Body(key).GetComponent<Renderer>().forceRenderingOff, "real mine entrance sensor starts hidden transport");
                Check(r.View.Root.transform.Find("Minecart") != null && r.View.Root.transform.Find("TimberBeam") != null, "runtime package contains the parked cart and artificial mine frame");
                r.Complete(key, 0);
                VectorNear(r.Down * 2, r.Body(key).velocity, "physical entry preserves measured incoming speed");
                Check(!r.Body(key).GetComponent<Renderer>().forceRenderingOff, "physical return reveals the original ball");
            }
        }

        private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception("Gold Rush: " + message); }
        private static void Near(double expected, double actual, string message) => Check(Math.Abs(expected - actual) < .0001, message + ": " + expected + " vs " + actual);
        private static void VectorNear(Vector3 expected, Vector3 actual, string message) => Check((expected - actual).magnitude < .0001f, message + ": " + expected + " vs " + actual);

        private sealed class Rig : IDisposable
        {
            internal static readonly Vector3 Spin = new Vector3(1, 2, 3);
            internal readonly Scene Scene;
            internal readonly GameObject Board;
            internal readonly MarbleSimulation Simulation;
            internal readonly MarblePhysicsBridge Bridge;
            internal readonly Entity Device;
            internal readonly MResLoadHandle View;
            private readonly List<MResLoadHandle> views = new List<MResLoadHandle>();
            private int launches;
            internal SimulationContext C => Simulation.Context;
            internal int Id => View.Root.GetComponent<MResDeviceBody>().InstanceId;
            internal DevicePose Pose { get { Bridge.TryGetDevicePose(Id, out var pose); return pose; } }
            internal Vector3 Down => Vector3.ProjectOnPlane(Vector3.down, Pose.Up).normalized;
            internal int Remaining => DeviceDurabilitySystem.Remaining(C, C.Manager.GetComponentData<OwnedDeviceData>(Device));
            internal Rig(int durability = 20, double duration = 10)
            {
                Scene = SceneManager.CreateScene("GoldRushValidation_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var balance = GameBalanceLoader.Load(); foreach (var d in balance.Devices) d.StartingCount = 0;
                balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 1000;
                balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue = 60;
                var definition = balance.Devices.Single(d => d.Id == 9011); definition.Durability = durability; definition.Value = duration;
                Simulation = new MarbleSimulation(balance); Bridge = new MarblePhysicsBridge(Scene, null, null, Simulation.EnqueueContact);
                Simulation.StartSession(Bridge); Simulation.BeginRound();
                Board = new GameObject("GoldRushBoard"); SceneManager.MoveGameObjectToScene(Board, Scene);
                Board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 37, 0));
                View = Add(Vector3.zero); CampaignStateUtility.FindDevice(C, Id, out Device); Tick();
            }
            internal MResLoadHandle Add(Vector3 offset)
            {
                var definition = C.Balance.Devices.Single(d => d.Id == 9011); CampaignStateUtility.CreateDevice(C, definition, definition.Price);
                var owned = Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last(); CampaignStateUtility.FindDevice(C, owned.InstanceId, out var entity);
                var data = C.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; C.Manager.SetComponentData(entity, data);
                var view = MResDeviceView.Create(owned, Board.transform, C.DeviceScripts.Get(owned.InstanceId, definition.Id));
                view.Root.transform.localPosition = offset; views.Add(view); view.Root.SetActive(true); Physics.SyncTransforms(); return view;
            }
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            internal bool Held(MarbleKey key) => C.Marbles.TryGetValue(key, out var entity) && C.Manager.HasComponent<HeldMotion>(entity);
            internal MarbleBody Marble(MarbleKey key) => Scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key));
            internal Rigidbody Body(MarbleKey key) => Marble(key).Body;
            internal DeviceContactContext Api(MarbleKey key) => new DeviceContactContext(C, Ball(key), Device, View.Root.GetComponent<MResDeviceBody>().Contact(key, Body(key).position), Pose);
            internal MarbleKey Launch(Vector3? position = null)
            {
                Simulation.FireOnce(); Simulation.BeforePhysics(.1f, Bridge, position ?? new Vector3(3 + launches++ * .6f, 5, 0), Quaternion.identity); Simulation.AfterPhysics(Bridge);
                var key = C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Body(key).drag = Body(key).angularDrag = 0; return key;
            }
            internal void Contact(MarbleKey key) => Contact(new[] { key });
            internal void Contact(IEnumerable<MarbleKey> keys, MResDeviceBody source = null)
            {
                Simulation.BeforePhysics(.1f, Bridge, Vector3.up * 10, Quaternion.identity);
                foreach (var key in keys)
                { Bridge.TrySetVelocity(key, new Vector3(3, 0, 4)); Body(key).angularVelocity = Spin;
                    Simulation.EnqueueContact((source ?? View.Root.GetComponent<MResDeviceBody>()).Contact(key, Body(key).position)); }
                Simulation.AfterPhysics(Bridge);
            }
            internal void Seed(int outcome)
            {
                for (uint seed = 1; seed < 100000; seed++)
                {
                    uint state = seed; if ((int)(MarbleRules.NextRandom(ref state) * 3) != outcome) continue;
                    var round = C.Round; round.RandomState = seed; C.Round = round; return;
                }
                throw new Exception("Missing seed for requested outcome.");
            }
            internal void Complete(MarbleKey key, int outcome)
            {
                Seed(outcome); var round = C.Round; round.Time = C.Manager.GetComponentData<HeldMotion>(Ball(key)).ReleaseAt - .1f; C.Round = round; Step();
            }
            internal void Tick() => DeviceScriptSystem.DeviceEvent(C, Id, "OnTick");
            internal void Step(bool physics = false)
            { Simulation.BeforePhysics(.1f, Bridge, Vector3.up * 10, Quaternion.identity); if (physics) Bridge.Simulate(.1f); Simulation.AfterPhysics(Bridge); }
            public void Dispose()
            { foreach (var view in views) view.Dispose(); Simulation.Dispose(); Bridge.Dispose(); UnityEngine.Object.DestroyImmediate(Board); SceneManager.UnloadSceneAsync(Scene); }
        }
    }
}
