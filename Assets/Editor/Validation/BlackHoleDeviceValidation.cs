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
    public static class BlackHoleDeviceValidation
    {
        private static int assertions;
        public static void Validate()
        {
            assertions = 0;
            ChargeAndSettlement();
            ForcesAndDuration();
            IndependenceAndLifecycle();
            SettlementApi();
            Durability();
            Debug.Log("BLACK_HOLE_VALIDATION_PASSED: " + assertions + " assertions; packaged Lua, charge, local-scene clicking, exact settlement, radial decay, duration and lifecycle.");
        }

        private static void ChargeAndSettlement()
        {
            using (var r = new Rig())
            {
                Check(r.View.Root.GetComponentsInChildren<Collider>().Where(c => c.name != "PlacementVolume").ToArray().Single().isTrigger, "core is a non-solid cylindrical sensor");
                r.Step(); Check(r.Status.Contains("0/10"), "uncharged status is visible even with no balls");
                Check(r.Simulation.TryActivateDevice(r.Id), "generic activation dispatch is available without a ball");
                Check(r.Status.Contains("0/10"), "early click cannot activate");
                for (int i = 1; i <= 9; i++)
                {
                    var key = r.Launch(new Vector3(0, .08f, 0)); r.Step();
                    Check(!r.C.Marbles.ContainsKey(key), "real cylinder contact consumes charging ball " + i);
                    Check(r.Status.Contains(i + "/10"), "each distinct swallowed ball contributes one charge");
                }
                Check(!r.Simulation.TryDequeueScore(out _), "charging produces no score events");
                Near(0, r.C.Round.Score, "charging has no score");
                Check(r.Simulation.TryActivateDevice(r.Id) && r.Status.Contains("9/10"), "nine balls cannot activate");
                var last = r.Launch(new Vector3(0, .08f, 0)); r.Step();
                Check(!r.C.Marbles.ContainsKey(last) && r.Status.Contains("点击"), "tenth ball readies without auto-activation");
                var extra = r.Launch(new Vector3(0, .08f, 0)); r.Step();
                Check(r.C.TryGetFlying(extra, out _), "full charge does not swallow extra balls before clicking");
                VectorNear(Vector3.zero, r.Force(extra), "no force while waiting for click");
                r.Consume(extra);
                var ray = new Ray(r.View.Root.transform.TransformPoint(new Vector3(0, 5, 0)), -r.Board.transform.up);
                Check(r.Bridge.TryPickDevice(ray, out int picked) && picked == r.Id, "local physics picking includes the packaged trigger");
                Check(r.Simulation.TryActivateDevice(picked) && r.Status.Contains("引力"), "click starts the charged effect");
                var ball = r.Launch(new Vector3(0, .08f, 0));
                r.Simulation.ApplyAttribute(ball, 961, GameAttribute.BALL_BASE_VALUE, AttributeOperation.Set, 7.5);
                r.Simulation.ApplyAttribute(ball, 962, GameAttribute.MACHINE_MULT, AttributeOperation.Set, 3);
                r.Simulation.ApplyGlobalAttribute(963, GameAttribute.GLOBAL_MULT, AttributeOperation.Set, 4);
                var round = r.C.Round; round.RushRemaining = 5; r.C.Round = round;
                r.Step();
                Check(!r.C.Marbles.ContainsKey(ball), "active hole removes a ball through the real sensor");
                Check(r.Simulation.TryDequeueScore(out var score), "direct settlement emits the normal score event");
                Near(125, score.Score, "temporary Rush value is included; unrelated global and machine multipliers are not applied again");
                Near(125, r.C.Round.Score, "score reaches the actual round account");
                Check(!score.IsPending && !r.Simulation.TryDequeueScore(out _), "one immediate settlement only");
                Check(score.Key.Equals(ball) && score.ZoneId == 100000 + r.Id, "event keeps ball and device identities");
            }
            using (var r = new Rig())
            {
                var keys = new List<MarbleKey>();
                // Distinct, non-overlapping spheres all touch the sensor in the same physical step.
                for (int z = -1; z <= 1; z++)
                    for (int x = -1; x <= 1; x++) keys.Add(r.Launch(new Vector3(x * .17f, -.025f, z * .17f)));
                keys.Add(r.Launch(new Vector3(-.085f, .145f, 0)));
                keys.Add(r.Launch(new Vector3(.085f, .145f, 0)));
                r.Step();
                Check(keys.Count(key => r.C.Marbles.ContainsKey(key)) == 1, "eleven same-step arrivals swallow exactly ten without device-wide cooldown");
                Check(r.Status.Contains("点击"), "batch contacts leave one full charge");
            }
        }

        private static void ForcesAndDuration()
        {
            foreach (float dt in new[] { .01f, .02f })
            using (var r = new Rig(dt))
            {
                r.Charge(); r.Simulation.TryActivateDevice(r.Id);
                double started = r.C.Round.Time;
                var key = r.Launch(new Vector3(1, .08f, 0));
                r.Simulation.ApplyAttribute(key, 970, GameAttribute.BALL_MASS, AttributeOperation.Set, 2);
                for (int i = 0; i < 4; i++)
                {
                    float distance = new[] { .5f, 1, 2, 20 }[i];
                    r.Move(key, new Vector3(distance, .08f, 0));
                    VectorNear(-r.Board.transform.right * (8 / (1 + distance * distance)), r.Force(key), "radial inverse-square-like decay at distance " + distance);
                }
                r.Move(key, new Vector3(0, .08f, 2));
                VectorNear(-r.Board.transform.forward * 1.6f, r.Force(key), "attraction comes from every board-plane direction");
                r.Move(key, new Vector3(0, .08f, 0));
                VectorNear(Vector3.zero, r.Force(key), "center is finite and has no undefined direction");
                r.Move(key, new Vector3(1, .08f, 0));
                r.Step();
                VectorNear(-r.Board.transform.right * (4 * dt / 2), r.Body(key).velocity, "actual PhysX integrates F dt / mass");
                Check(!r.C.Manager.GetComponentData<BallFeatures>(r.Ball(key)).Magnetic, "ordinary non-magnetic balls feel the effect");
                VectorNear(r.Body(key).velocity, r.C.Manager.GetComponentData<Motion3D>(r.Ball(key)).LinearVelocity, "force readback reaches ECS");
                r.Consume(key);
                while (r.C.Round.Time < started + 5) r.Step();
                Check(r.Simulation.TryActivateDevice(r.Id), "repeat click dispatches while active");
                while (r.C.Round.Time < started + 10 + dt) r.Step();
                Check(r.Status.Contains("0/10"), "no-ball timer expires at ten seconds; repeat clicks do not extend it");
                key = r.Launch(new Vector3(1, .08f, 0));
                VectorNear(Vector3.zero, r.Force(key), "force disappears on expiry");
                r.Consume(key);
                r.Charge(); Check(r.Simulation.TryActivateDevice(r.Id) && r.Status.Contains("引力"), "a second complete charge can activate again");
            }
        }

        private static void IndependenceAndLifecycle()
        {
            using (var r = new Rig())
            {
                var second = r.AddDevice(new Vector3(3, 0, 0));
                int secondId = second.Root.GetComponent<MResDeviceBody>().InstanceId;
                r.Charge(); r.Simulation.TryActivateDevice(r.Id);
                Check(r.Simulation.GetDeviceStatus(secondId).Contains("0/10"), "each package instance owns separate charge");
                Check(r.Simulation.TryActivateDevice(secondId) && r.Simulation.GetDeviceStatus(secondId).Contains("0/10"), "one charged device cannot activate another");
                var key = r.Launch(new Vector3(1, .08f, 0));
                double time = r.C.Round.Time;
                string status = r.Status;
                for (int i = 0; i < 3; i++) r.Simulation.GetDeviceStatus(r.Id);
                Near(time, r.C.Round.Time, "reading status cannot advance simulation time");
                Check(status == r.Status, "status reads cannot change timers");
                var contact = r.Api(key);
                Check(contact.Motion.TryHold(.1f), "generic hold works during attraction"); contact.Close();
                var position = r.Body(key).position; r.Step(); r.Step();
                VectorNear(position, r.Body(key).position, "suspended marbles are excluded from force integration");
                r.View.Root.SetActive(false);
                Check(!r.Simulation.TryActivateDevice(r.Id), "missing physical view rejects activation");
                r.View.Root.SetActive(true); Physics.SyncTransforms();
                var data = r.C.Manager.GetComponentData<OwnedDeviceData>(r.Device); data.Placed = false; r.C.Manager.SetComponentData(r.Device, data);
                Check(!r.Simulation.TryActivateDevice(r.Id), "inventory-only devices reject activation");
                data.Placed = true; r.C.Manager.SetComponentData(r.Device, data);
                var round = r.C.Round; round.Phase = (byte)RoundPhase.Build; r.C.Round = round;
                Check(!r.Simulation.TryActivateDevice(r.Id), "build phase rejects activation");
                r.Simulation.BeginRound(); r.Step();
                Check(r.Status.Contains("0/10"), "next round clears charge and active duration in Lua");
                Check(r.C.Marbles.Count == 0, "new round removes held marbles");
                Check(!r.Simulation.TryActivateDevice(int.MaxValue), "unknown instance rejects activation");
                var api = new DeviceEventContext(r.C, r.Device);
                api.SetStatus(new string('a', 300)); Check(r.Status.Length == 256, "generic status has a bounded length");
                api.Close(); Expired(() => api.SetStatus("stale")); Expired(() => { _ = api.Time; });
                Expired(() => { _ = api.Parameter; });
                r.Simulation.StartSession(r.Bridge);
                Check(r.Simulation.GetDeviceStatus(r.Id) == string.Empty, "session reset clears published status");
                Check(r.View.Root.GetComponent<MResDeviceBody>().Script.IsDisposed, "session reset disposes the old device Lua");
            }
        }

        private static void SettlementApi()
        {
            using (var r = new Rig())
            {
                var key = r.Launch(new Vector3(2, .08f, 0));
                var api = r.Api(key);
                Check(!api.TrySettle(double.NaN) && !api.TrySettle(double.PositiveInfinity) && !api.TrySettle(-1), "invalid amount rejected without consuming");
                Check(r.C.TryGetFlying(key, out _), "invalid request leaves ball alive");
                Check(api.TrySettle(1.75) && !api.TrySettle(100), "settlement is atomic and cannot be repeated in one callback");
                Near(1, r.C.Round.Score, "integer portion is credited"); Near(.75, r.C.Round.FractionalScore, "fraction retained");
                api.Close(); Expired(() => api.TrySettle(100));
                // A competing score zone in the same step must not pay again.
                r.C.Contacts.Add(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, Multiplier = 10 });
                ContactResolutionSystem.Execute(r.C); r.C.Contacts.Clear(); Near(1, r.C.Round.Score, "terminal zones cannot resettle consumed ball");
                r.Step(); Check(!r.C.Marbles.ContainsKey(key), "normal despawn releases the settled physics body");
                key = r.Launch(new Vector3(2, .08f, 0)); api = r.Api(key);
                Check(api.TrySettle(.5), "fractional awards work across balls"); api.Close(); r.Step();
                Near(2, r.C.Round.Score, "fractions carry into subsequent settlement"); Near(.25, r.C.Round.FractionalScore, "fraction remainder preserved");
                key = r.Launch(new Vector3(2, .08f, 0)); api = r.Api(key);
                Check(api.TrySettle(double.MaxValue), "large finite explicit award is capped safely"); api.Close(); r.Step();
                Near(Math.Min(r.C.Tuning.MaxRoundScore, 2 + r.C.Tuning.MaxPerMarbleScore), r.C.Round.Score, "existing per-ball and round score caps apply");
            }
        }

        private static void Durability()
        {
            foreach (int durability in new[] { 20, 1 })
            using (var r = new Rig(.02f, durability))
            {
                r.Charge();
                Near(durability, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "charging ten marbles costs no durability");
                Near(0, r.C.Round.Score, "charging earns no score");
                Check(r.Simulation.TryActivateDevice(r.Id), "charged hole starts its effect");
                double end = r.C.Round.Time + 10;
                Near(durability - 1, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "activation costs one use");
                if (durability == 1) Check(Presentation.DeviceDisplayText.Durability(r.Simulation.GetOwnedDevices().Single()).Contains("当前效果待完成"), "last active effect is not labelled already disabled");
                Check(r.Simulation.TryActivateDevice(r.Id), "repeated active click is handled without another activation");
                var key = r.Launch(new Vector3(2, .08f, 0)); r.Step(); r.Step();
                Check(r.Body(key).velocity.magnitude > 0, "activated hole attracts even after spending the final use");
                Near(durability - 1, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "attraction and repeated clicks cost no further uses");
                r.Consume(key); // Fixture cleanup is not a device effect.
                for (int i = 0; i < 25; i++)
                {
                    key = r.Launch(new Vector3(0, .08f, 0));
                    double before = r.C.Round.Score, value = r.ApiValue(key);
                    r.Step();
                    Check(!r.C.Marbles.ContainsKey(key), "every active arrival settles including arrivals beyond twenty");
                    Near(before + value * 10, r.C.Round.Score, "active swallow keeps its tenfold award");
                }
                Near(durability - 1, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "all scored swallows belong to one activation");
                var state = r.C.Round; state.Time = end - .12; r.C.Round = state;
                key = r.Launch(new Vector3(2, .08f, 0)); r.Step();
                Check(r.Body(key).velocity.magnitude > 0 && r.View.Root.GetComponentsInChildren<Collider>().Where(c => c.name != "PlacementVolume").ToArray().All(c => c.enabled), "effect and sensor still work near the full ten-second deadline");
                r.Consume(key);
                state = r.C.Round; state.Time = end + .02; r.C.Round = state; r.Step();
                key = r.Launch(new Vector3(2, .08f, 0)); r.Step();
                VectorNear(Vector3.zero, r.Body(key).velocity, "attraction stops after ten seconds");
                if (durability == 1)
                {
                    Check(!r.Simulation.TryActivateDevice(r.Id), "exhausted hole rejects activation after completing the effect");
                    Check(r.View.Root.GetComponentsInChildren<Collider>().Where(c => c.name != "PlacementVolume").ToArray().All(c => !c.enabled), "completed exhausted hole disables its sensor");
                    r.Move(key, new Vector3(0, .08f, 0)); r.Step();
                    Check(r.C.TryGetFlying(key, out _), "exhaustion prevents new charging");
                }
                else { r.Consume(key); r.Charge(); Check(r.Simulation.TryActivateDevice(r.Id), "next charge can activate again"); Near(18, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "second complete charge costs only its activation"); }
                var round = r.C.Round; round.Phase = (byte)RoundPhase.Build; r.C.Round = round;
                r.Simulation.BeginRound(); r.Step();
                Near(durability, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "new round restores the full metadata durability");
                Check(r.Status.Contains("0/10") && r.View.Root.GetComponentsInChildren<Collider>().Where(c => c.name != "PlacementVolume").ToArray().All(c => c.enabled), "new round restores charging and collisions");
            }
        }

        private static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception("Black hole: " + message); }
        private static void Near(double expected, double actual, string message) => Check(Math.Abs(expected - actual) < .0002, message + ": " + expected + " vs " + actual);
        private static void VectorNear(Vector3 expected, Vector3 actual, string message) => Check((expected - actual).magnitude < .0005f, message + ": " + expected + " vs " + actual);
        private static void Expired(Action action)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "callback context expires"); }

        private sealed class Rig : IDisposable
        {
            internal readonly float Dt;
            internal readonly Scene Scene;
            internal readonly GameObject Board;
            internal readonly MarbleSimulation Simulation;
            internal readonly MarblePhysicsBridge Bridge;
            internal readonly Entity Device;
            internal readonly MResLoadHandle View;
            internal readonly int Id;
            private readonly List<MResLoadHandle> views = new List<MResLoadHandle>();
            internal SimulationContext C => Simulation.Context;
            internal string Status => Simulation.GetDeviceStatus(Id);
            internal Rig(float dt = .02f, int durability = -1)
            {
                Dt = dt;
                Scene = SceneManager.CreateScene("BlackHoleValidation_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var balance = GameBalanceLoader.Load(); foreach (var d in balance.Devices) d.StartingCount = 0;
                balance.Devices.Single(d => d.Id == 9005).Durability = durability;
                balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 1000; balance.Marble.FireIntervalSeconds = balance.Marble.MaxFireIntervalSeconds = .01f;
                balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue = 60;
                Simulation = new MarbleSimulation(balance); Bridge = new MarblePhysicsBridge(Scene, null, null, Simulation.EnqueueContact);
                Simulation.StartSession(Bridge); Simulation.BeginRound();
                Simulation.ApplyGlobalAttribute(980, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                Board = new GameObject("BlackHoleBoard"); SceneManager.MoveGameObjectToScene(Board, Scene);
                Board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(50, 37, 13));
                View = AddDevice(Vector3.zero); Id = View.Root.GetComponent<MResDeviceBody>().InstanceId;
                CampaignStateUtility.FindDevice(C, Id, out Device);
            }
            internal MResLoadHandle AddDevice(Vector3 offset)
            {
                var definition = C.Balance.Devices.Single(d => d.Id == 9005);
                CampaignStateUtility.CreateDevice(C, definition, definition.Price);
                var owned = Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
                CampaignStateUtility.FindDevice(C, owned.InstanceId, out var entity);
                var data = C.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; C.Manager.SetComponentData(entity, data);
                var view = MResDeviceView.Create(owned, Board.transform, C.DeviceScripts.Get(owned.InstanceId, definition.Id));
                view.Root.transform.localPosition = offset; views.Add(view); view.Root.SetActive(true); Physics.SyncTransforms(); return view;
            }
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            internal double ApiValue(MarbleKey key) { var api = Api(key); try { return api.Value; } finally { api.Close(); } }
            internal MarbleBody Marble(MarbleKey key) => Scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key));
            internal Rigidbody Body(MarbleKey key) => Marble(key).Body;
            internal DeviceContactContext Api(MarbleKey key)
            {
                Bridge.TryGetDevicePose(Id, out var pose);
                return new DeviceContactContext(C, Ball(key), Device, View.Root.GetComponent<MResDeviceBody>().Contact(key, Body(key).position), pose);
            }
            internal Vector3 Force(MarbleKey key)
            {
                var body = View.Root.GetComponent<MResDeviceBody>();
                var api = new DeviceFieldContext(body, Marble(key), Dt);
                try { body.Script.Invoke("OnField", api); return api.Force; } finally { api.Close(); }
            }
            internal MarbleKey Launch(Vector3 position)
            {
                Simulation.FireOnce(); Simulation.BeforePhysics(Dt, Bridge, new Vector3(4, 5, 0), Quaternion.identity); Simulation.AfterPhysics(Bridge);
                var key = C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Body(key).drag = Body(key).angularDrag = 0;
                Move(key, position); return key;
            }
            internal void Move(MarbleKey key, Vector3 position) =>
                Check(Bridge.TryMove(key, Board.transform.TransformPoint(position), Vector3.zero), "test ball can move to the desired point");
            internal void Consume(MarbleKey key)
            { BallInteractionSystem.Consume(C, Ball(key), false); DespawnSystem.Execute(C, Bridge); }
            internal void Charge()
            {
                for (int i = 0; i < 10; i++) { Launch(new Vector3(0, .08f, 0)); Step(); }
                Check(Status.Contains("点击"), "ten actual contacts fully charge");
            }
            internal void Step()
            { Simulation.BeforePhysics(Dt, Bridge, Vector3.up * 10, Quaternion.identity); Bridge.Simulate(Dt); Simulation.AfterPhysics(Bridge); }
            public void Dispose()
            {
                foreach (var view in views) view.Dispose();
                Simulation.Dispose(); Bridge.Dispose(); UnityEngine.Object.DestroyImmediate(Board); SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }
}
