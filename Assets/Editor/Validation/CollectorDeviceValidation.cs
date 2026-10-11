using System;
using System.Linq;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    // Executes the packaged Lua through the normal fixed-step loop in an isolated PhysX scene.
    public static class CollectorDeviceValidation
    {
        private static int assertions;
        public static void Validate()
        {
            assertions = 0;
            GroupAndRelease();
            RangeAndLifecycle();
            ObstructedAndUnfinished();
            Debug.Log("COLLECTOR_VALIDATION_PASSED: " + assertions + " assertions; packaged Lua, gradual attraction, shared deadline, separated slots and cleanup.");
        }

        private static void GroupAndRelease()
        {
            using (var r = new Rig())
            {
                var keys = new[] { r.Launch(), r.Launch(), r.Launch() };
                Check(keys.Distinct().Count() == keys.Length, "fixture spawns distinct balls");
                var speeds = new[] { 0f, .03f, 5f };
                r.Simulation.ApplyAttribute(keys[2], 997, GameAttribute.BALL_RADIUS, AttributeOperation.Set, .75);
                for (int i = 0; i < keys.Length; i++) r.Place(keys[i], .8f, i * 120, speeds[i]);
                var initial = keys.Select(k => r.Body(k).position).ToArray();
                r.Step(false);
                double deadline = r.C.Round.Time + 2;
                foreach (var key in keys) Check(!r.Held(key), "distant entrants start moving before holding");
                r.Step();
                for (int i = 0; i < keys.Length; i++)
                    Check(Vector3.Distance(initial[i], r.Body(keys[i]).position) > .05f, "attraction moves through real physics");
                for (int i = 0; i < 12; i++) r.Step();
                foreach (var key in keys)
                {
                    Check(r.Held(key), "all simultaneous entrants gather without a device-wide per-ball cooldown");
                    Near(deadline, r.C.Manager.GetComponentData<HeldMotion>(r.Ball(key)).ReleaseAt, "common magnetic deadline");
                    Check(r.Body(key).isKinematic && !r.Body(key).detectCollisions, "gathered ball is suspended");
                }
                for (int i = 0; i < keys.Length; i++)
                    for (int j = i + 1; j < keys.Length; j++)
                        Check(Vector3.Distance(r.Body(keys[i]).position, r.Body(keys[j]).position) >= r.Radius(keys[i]) + r.Radius(keys[j]),
                            "different-sized balls reserve non-overlapping cluster positions: " + i + "/" + j + " positions " +
                            r.Body(keys[i]).position.ToString("F5") + "/" + r.Body(keys[j]).position.ToString("F5") +
                            " radii " + r.Radius(keys[i]) + "/" + r.Radius(keys[j]));

                var late = r.Launch(); r.Place(late, .65f, 70, 2); r.Step(false);
                for (int i = 0; i < 7; i++) r.Step();
                Check(r.Held(late), "later entrant joins the active group");
                Near(deadline, r.C.Manager.GetComponentData<HeldMotion>(r.Ball(late)).ReleaseAt, "late entrant does not extend the group");
                keys = keys.Concat(new[] { late }).ToArray(); speeds = speeds.Concat(new[] { 2f }).ToArray();
                var positions = keys.Select(k => r.Body(k).position).ToArray();
                var spins = keys.Select(k => r.C.Manager.GetComponentData<HeldMotion>(r.Ball(k)).OriginalSpin).ToArray();
                while (r.C.Round.Time + Rig.StepSeconds < deadline - .000001)
                {
                    r.Step();
                    for (int i = 0; i < keys.Length; i++)
                    { Check(r.Held(keys[i]), "no early group release"); VectorNear(positions[i], r.Body(keys[i]).position, "cluster remains stationary"); }
                }
                r.Step();
                for (int i = 0; i < keys.Length; i++)
                {
                    var key = keys[i]; var body = r.Body(key);
                    Check(!r.Held(key) && !body.isKinematic && body.detectCollisions, "whole group resumes in the same fixed step");
                    VectorNear(r.Down * speeds[i], body.velocity, "each ball keeps its entry speed including zero and low speed");
                    VectorNear(positions[i], body.position, "release retains separate cluster positions");
                    VectorNear(body.velocity, r.C.Manager.GetComponentData<Motion3D>(r.Ball(key)).LinearVelocity, "ECS velocity matches release");
                    VectorNear(spins[i], body.angularVelocity, "release restores spin captured after attraction and ordinary collisions");
                }
                r.Step(false);
                for (int i = 0; i < keys.Length; i++)
                {
                    VectorNear(r.Down * speeds[i], r.Body(keys[i]).velocity, "released balls are not immediately caught again");
                    Check(r.VisitCount(keys[i]) == 1, "one admission per ball per device");
                }
                // A new ball starts another independent two-second group.
                for (int i = 0; i < keys.Length; i++)
                    Check(r.Bridge.TryMove(keys[i], new Vector3(5 + i, 8, 0), Vector3.zero), "clear previously released balls from the collection area");
                var next = r.Launch(); r.Place(next, .7f, 0, 1); r.Step(false);
                double nextDeadline = r.C.Round.Time + 2;
                for (int i = 0; i < 8; i++) r.Step();
                Check(r.Held(next), "subsequent group can start");
                Near(nextDeadline, r.C.Manager.GetComponentData<HeldMotion>(r.Ball(next)).ReleaseAt, "subsequent group has its own deadline");
            }
        }

        private static void RangeAndLifecycle()
        {
            using (var r = new Rig())
            {
                var outside = r.Launch(); r.Place(outside, 1.3f, 0, 2); r.Step(false);
                VectorNear(r.Pose.Forward * 2, r.Body(outside).velocity, "outside range unaffected");
                Check(r.VisitCount(outside) == 0, "outside range consumes no visit");
                var high = r.Launch(); r.Place(high, 0, 0, 2, .5f); r.Step(false);
                Check(!r.Held(high) && r.VisitCount(high) == 0, "ball above the board is outside the field");
                var key = r.Launch(); r.Place(key, 0, 0, 5); r.Step(false);
                Check(r.Held(key), "ball at center holds immediately");
                var api = r.Api(key);
                Check(api.BallId == key.ToString() && api.RoundId == key.RoundId, "stable string identity includes round and full spawn sequence");
                Near(Rig.StepSeconds, api.DeltaTime, "fixed simulation delta exposed");
                Near(r.Radius(key), api.Radius, "radius uses world units"); Near(1.2, api.Range, "range comes from package");
                api.Close();
                Expired(() => { _ = api.BallId; }); Expired(() => { _ = api.RoundId; });
                Expired(() => { _ = api.DeltaTime; }); Expired(() => { _ = api.Radius; }); Expired(() => { _ = api.Range; });
                r.Simulation.ApplyAttribute(key, 996, GameAttribute.BALL_MAX_SPEED, AttributeOperation.Set, 1);
                for (int i = 0; i < 40; i++) r.Step(false);
                VectorNear(r.Down, r.Body(key).velocity, "release observes current speed cap");
            }
            foreach (bool remove in new[] { false, true })
                using (var r = new Rig())
                {
                    var key = r.Launch(); r.Place(key, 0, 0, 2); r.Step(false);
                    Check(r.Held(key), "lifecycle setup held");
                    var entryVelocity = r.C.Manager.GetComponentData<HeldMotion>(r.Ball(key)).OriginalVelocity;
                    if (remove) r.C.Manager.DestroyEntity(r.Device);
                    else { var data = r.C.Manager.GetComponentData<OwnedDeviceData>(r.Device); data.Placed = false; r.C.Manager.SetComponentData(r.Device, data); }
                    r.Step(false);
                    Check(!r.Held(key) && r.Body(key).detectCollisions, "removed or recalled source cancels suspension");
                    VectorNear(entryVelocity, r.Body(key).velocity, "cancel restores pre-hold motion without release callback");
                }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Place(key, 0, 0, 2); r.Step(false);
                r.Simulation.ApplyAttribute(key, 995, GameAttribute.BALL_LIFETIME, AttributeOperation.Set, 1);
                for (int i = 0; i < 20; i++) r.Step(false);
                Check(!r.C.Marbles.ContainsKey(key), "held ball still expires normally");
                var next = r.Launch();
                Check(!r.Held(next) && !r.Body(next).isKinematic && r.Body(next).detectCollisions, "pool reuse has no hold state");
                r.Place(next, 0, 0, 2); r.Step(false);
                for (int i = 0; i < 6; i++) r.Step();
                Check(r.Held(next), "new identity can join despite a previous expired ball");
                var round = r.C.Round; round.Phase = (byte)RoundPhase.Build; r.C.Round = round;
                Check(r.Simulation.BeginRound(), "restart round while retaining device instance and script");
                Check(r.C.Marbles.Count == 0 && r.Bridge.ActiveBodyCount == 0, "round reset removes all suspended balls");
                var fresh = r.Launch(); r.Place(fresh, 0, 0, 3); r.Step(false);
                Check(r.Held(fresh), "Lua state resets for the next round");
                Near(r.C.Round.Time + 2, r.C.Manager.GetComponentData<HeldMotion>(r.Ball(fresh)).ReleaseAt, "new round ignores old magnetic timer");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Place(key, 0, 0, 2);
                r.Simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Drain, TargetId = 99 });
                r.Step(false);
                Check(!r.C.Marbles.ContainsKey(key), "update callback cannot rescue an already settled ball");
            }
        }

        private static void ObstructedAndUnfinished()
        {
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Place(key, .12f, 0, 2);
                var obstacle = new GameObject("BlockedCollectionSlot"); SceneManager.MoveGameObjectToScene(obstacle, r.Scene);
                obstacle.transform.position = r.Pose.Position + r.Pose.Up * (r.Radius(key) + .005f);
                obstacle.AddComponent<SphereCollider>().radius = .015f; Physics.SyncTransforms();
                try
                {
                    r.Step(false); double end = r.C.Round.Time + 2;
                    Check(!r.Held(key), "obstructed target does not teleport into solids");
                    while (r.C.Round.Time < end - .000001) r.Step(false);
                    Check(!r.Held(key), "failed gathering never leaves an indefinite hold");
                    VectorNear(r.Down * 2, r.Body(key).velocity, "unfinished attraction releases downhill at the same deadline");
                }
                finally { UnityEngine.Object.DestroyImmediate(obstacle); }
            }
        }

        private static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new Exception("Collector: " + message); }
        private static void Near(double expected, double actual, string message) => Check(Math.Abs(expected - actual) < .0001, message + ": " + expected + " vs " + actual);
        private static void VectorNear(Vector3 expected, Vector3 actual, string message) => Check((expected - actual).magnitude < .0005f, message + ": " + expected + " vs " + actual);
        private static void Expired(Action action)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "new callback properties expire with their context"); }

        private sealed class Rig : IDisposable
        {
            internal const float StepSeconds = .05f;
            internal static readonly Vector3 Spin = new Vector3(1, 2, 3);
            internal readonly Scene Scene;
            internal readonly GameObject Board;
            internal readonly MarbleSimulation Simulation;
            internal readonly MarblePhysicsBridge Bridge;
            internal readonly Entity Device;
            internal readonly MResLoadHandle View;
            internal readonly OwnedDeviceSnapshot Owned;
            internal SimulationContext C => Simulation.Context;
            internal DevicePose Pose { get { Bridge.TryGetDevicePose(Owned.InstanceId, out var pose); return pose; } }
            internal Vector3 Down => Vector3.ProjectOnPlane(Vector3.down, Pose.Up).normalized;
            private int launches;
            internal Rig()
            {
                Scene = SceneManager.CreateScene("CollectorValidation_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var balance = GameBalanceLoader.Load();
                foreach (var d in balance.Devices) d.StartingCount = 0;
                balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 1000;
                balance.Marble.FireIntervalSeconds = .01f;
                balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue = 60;
                Simulation = new MarbleSimulation(balance);
                Bridge = new MarblePhysicsBridge(Scene, null, null, Simulation.EnqueueContact);
                Simulation.StartSession(Bridge); Simulation.BeginRound();
                Simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var definition = C.Balance.Devices.Single(d => d.Id == 9003);
                CampaignStateUtility.CreateDevice(C, definition, definition.Price);
                Owned = Simulation.GetOwnedDevices().Single();
                CampaignStateUtility.FindDevice(C, Owned.InstanceId, out Device);
                var data = C.Manager.GetComponentData<OwnedDeviceData>(Device); data.Placed = true; C.Manager.SetComponentData(Device, data);
                Board = new GameObject("CollectorBoard"); SceneManager.MoveGameObjectToScene(Board, Scene);
                Board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 47, 0));
                View = MResDeviceView.Create(Owned, Board.transform, C.DeviceScripts.Get(Owned.InstanceId, definition.Id));
                View.Root.SetActive(true); Physics.SyncTransforms();
                Check(View.Root.GetComponentInChildren<MeshCollider>().isTrigger, "package has a cylinder sensor rather than a solid obstruction");
            }
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            internal bool Held(MarbleKey key) => C.Marbles.TryGetValue(key, out var entity) && C.Manager.HasComponent<HeldMotion>(entity);
            internal Rigidbody Body(MarbleKey key) => Scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
            internal float Radius(MarbleKey key) => (float)AttributeRuntime.Get(C, Ball(key), GameAttribute.BALL_RADIUS) * AttributeRuntime.RadiusToWorld;
            internal MarbleKey Launch()
            {
                Simulation.FireOnce(); Simulation.BeforePhysics(StepSeconds, Bridge, new Vector3(4 + launches++ * .6f, 5, 0), Quaternion.identity);
                Simulation.AfterPhysics(Bridge);
                var key = C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Body(key).drag = Body(key).angularDrag = 0;
                return key;
            }
            internal void Place(MarbleKey key, float distance, float angle, float speed, float height = 0)
            {
                var direction = Quaternion.AngleAxis(angle, Pose.Up) * Pose.Forward;
                Check(Bridge.TryMove(key, Pose.Position + Pose.Up * (Radius(key) + .005f + height) + direction * distance, Pose.Forward * speed), "fixture places the ball without obstruction");
                Body(key).angularVelocity = Spin;
            }
            internal DeviceContactContext Api(MarbleKey key) => new DeviceContactContext(C, Ball(key), Device,
                View.Root.GetComponent<MResDeviceBody>().Contact(key, Body(key).position), Pose);
            internal int VisitCount(MarbleKey key)
            {
                var visits = C.Manager.GetBuffer<DeviceVisit>(Ball(key));
                for (int i = 0; i < visits.Length; i++) if (visits[i].Key == 100000 + Owned.InstanceId) return visits[i].Count;
                return 0;
            }
            internal void Step(bool simulate = true)
            {
                Simulation.BeforePhysics(StepSeconds, Bridge, Vector3.up * 10, Quaternion.identity);
                if (simulate) Bridge.Simulate(StepSeconds);
                Simulation.AfterPhysics(Bridge);
            }
            public void Dispose()
            {
                View.Dispose(); Simulation.Dispose(); Bridge.Dispose();
                UnityEngine.Object.DestroyImmediate(Board); SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }
}
