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
        private static OwnedDeviceData AddSplitter(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9015));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var e);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(e);
        }
        private static void SplitterLuaApi()
        {
            int start = assertions;
            foreach (int wear in new[] { 1, 20, -1 })
                using (var f = new Fixture())
                {
                    f.C.Balance.Devices.Single(d => d.Id == 9015).Durability = wear;
                    var device = AddSplitter(f); var key = f.Launch(); var parent = f.Ball(key);
                    double blood = f.C.Player.Blood, mass = f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_MASS);
                    double radius = f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_RADIUS);
                    float speed = f.Physics.States[key].LinearVelocity.magnitude;
                    long expiry = f.C.Manager.GetComponentData<Lifetime>(parent).ExpireTick;
                    var api = CashApi(f, key, device); api.AddScoreMultiplier(1.5); api.AddScoreMultiplier(1.5); api.Close();
                    var score = f.C.Manager.GetComponentData<MarbleScore>(parent); score.ScoreBonus = 1; f.C.Manager.SetComponentData(parent, score);
                    f.C.Manager.GetBuffer<MarbleModifier>(parent).Add(new MarbleModifier { Attribute = MarbleModifierAttribute.ScoreAdd, Value = 3, Stacks = 1 });
                    f.Step(f.Hit(key, device), f.Hit(key, device));
                    Check(!f.C.Marbles.ContainsKey(key) && f.C.Marbles.Count == 3, "split atomically replaces parent with three children");
                    Near(3, f.C.Round.ActiveMarbleCount, "active counter includes only surviving children after cleanup");
                    Near(blood, f.C.Player.Blood, "division costs and refunds no blood");
                    Near(0, f.C.Round.Score, "division does not settle score early");
                    var children = f.C.Marbles.Keys.OrderBy(k => k.Sequence).ToArray();
                    for (int i = 0; i < children.Length; i++)
                    {
                        var child = f.Ball(children[i]);
                        Near(10d / 3, f.Simulation.GetMarbleAttribute(children[i], GameAttribute.BALL_BASE_VALUE), "each child gets one third value");
                        Near(radius * radius * radius / 3, Math.Pow(f.Simulation.GetMarbleAttribute(children[i], GameAttribute.BALL_RADIUS), 3), "each child gets one third volume");
                        Near(mass / 3, f.Simulation.GetMarbleAttribute(children[i], GameAttribute.BALL_MASS), "mass is conserved");
                        Near(2, f.C.Manager.GetComponentData<MarbleScore>(child).ScoreMultiplier, "factor-minus-one accumulation inherited unchanged");
                        Near(expiry, f.C.Manager.GetComponentData<Lifetime>(child).ExpireTick, "split does not extend lifetime");
                        VectorNear(Quaternion.AngleAxis((i - 1) * 30, Vector3.up) * Vector3.forward * speed, f.Physics.States[children[i]].LinearVelocity, "three configured directions preserve speed");
                        var query = CashApi(f, children[i], device); Check(query.HasSplitFrom(device.InstanceId), "lineage records source instance"); query.Close();
                    }
                    f.Step(children.Select(k => f.Hit(k, device)).ToArray());
                    Check(f.C.Marbles.Count == 3, "returning children cannot split again at their source");
                    Near(wear == -1 ? -1 : wear - 1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "complete batch spends one use; self returns are free");
                    f.Step(children.Select(k => new MarbleContact { Key = k, TargetId = 700, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true }).ToArray());
                    Near(28, f.C.Round.Score + f.C.Round.FractionalScore, "value, flat bonus and accumulated multiplier settle without tripling flat bonuses");
                }
            using (var f = new Fixture())
            {
                var a = AddSplitter(f); var b = AddSplitter(f); var key = f.Launch();
                f.Step(f.Hit(key, a)); var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).First();
                f.Step(f.Hit(child, b)); Check(f.C.Marbles.Count == 5, "a different splitter can divide a child");
                var descendants = f.C.Marbles.Keys.Where(k => f.C.Manager.GetComponentData<BallFeatures>(f.Ball(k)).Generation == 2).ToArray();
                Check(descendants.Length == 3, "second splitter makes three grandchildren");
                foreach (var k in descendants)
                {
                    var api = CashApi(f, k, a); Check(api.HasSplitFrom(a.InstanceId) && api.HasSplitFrom(b.InstanceId), "all ancestor instances survive multiple splits"); api.Close();
                }
                f.Step(descendants.Select(k => f.Hit(k, a)).ToArray());
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == a.InstanceId).RemainingDurability, "grandchildren do not spend source durability");
                var copyKey = descendants[0]; var copyApi = CashApi(f, copyKey, b); copyApi.Children(1, false); copyApi.Close(); f.Step();
                var copy = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); var query = CashApi(f, copy, a);
                Check(query.HasSplitFrom(a.InstanceId) && query.HasSplitFrom(b.InstanceId), "copies inherit splitter ancestry too"); query.Close();
                int count = f.C.Marbles.Count; f.Step(f.Hit(copy, a)); Near(count, f.C.Marbles.Count, "copied descendants pass through");
                var fresh = f.Launch(); var freshApi = CashApi(f, fresh, a); Check(!freshApi.HasSplitFrom(a.InstanceId), "new launch starts with empty split ancestry"); freshApi.Close();
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "splitter supports new round");
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == a.InstanceId).RemainingDurability, "new round restores splitter durability");
            }
            for (int failedChild = 1; failedChild <= 3; failedChild++)
                using (var f = new Fixture())
                {
                    var device = AddSplitter(f); var key = f.Launch(); var before = f.Physics.States[key];
                    f.Physics.FailSpawnAt = f.Physics.Attempts + failedChild;
                    f.Step(f.Hit(key, device));
                    Check(f.C.Marbles.Count == 1 && f.C.Marbles.ContainsKey(key) && f.Physics.Spawns.Count == 1, "any failed child rolls back the complete batch");
                    Near(1, f.C.Round.ActiveMarbleCount, "rollback restores active count");
                    VectorNear(before.Position, f.Physics.States[key].Position, "failed split retains parent position");
                    VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "failed split retains parent velocity");
                    Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "failed split retains full value");
                    Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "failed batch is free");
                    f.Physics.FailSpawnAt = 0; f.Step(f.Hit(key, device)); Check(f.C.Marbles.Count == 3, "later reentry can retry failed division");
                }
            foreach (int capacity in new[] { 2, 3 })
                using (var f = new Fixture())
                {
                    var device = AddSplitter(f); var key = f.Launch(); f.C.Tuning.MaxActiveMarbles = capacity;
                    f.Step(f.Hit(key, device)); Near(capacity == 3 ? 3 : 1, f.C.Marbles.Count, "capacity uses net replacement count");
                    Near(capacity == 3 ? 19 : 20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "capacity failure costs nothing");
                }
            using (var f = new Fixture())
            {
                var device = AddSplitter(f); var key = f.Launch(); var api = CashApi(f, key, device);
                ExpectArgument(() => api.CreateSplitBatch(1)); ExpectArgument(() => api.CreateSplitBatch(257));
                var batch = api.CreateSplitBatch(3);
                Check(!batch.TryAdd(Vector3.one * float.NaN, Vector3.zero), "nonfinite position rejected");
                Check(batch.TryAdd(Vector3.one * 3, Vector3.zero), "zero velocity is a valid split output");
                Check(!batch.TryAdd(Vector3.one * 3, Vector3.one), "overlapping child positions rejected");
                Check(!batch.TryCommit(), "incomplete batch never consumes parent");
                api.Close(); ExpectExpired(() => batch.TryCommit()); ExpectExpired(() => { _ = batch.Radius; });
                api = CashApi(f, key, device); batch = api.CreateSplitBatch(3); api.Set("BALL_BASE_VALUE", 2);
                Check(!batch.CanSplit, "changing attributes invalidates the split snapshot"); api.Close();
                api = CashApi(f, key, device); api.Set("BALL_MASS", .1); api.Close(); f.Step(f.Hit(key, device));
                Check(f.C.Marbles.ContainsKey(key), "too-small mass is rejected instead of duplicating mass through clamping");
                api = CashApi(f, key, device); api.Set("BALL_MASS", 1); api.Set("BALL_BASE_VALUE", 0); api.Motion.TrySetVelocity(Vector3.zero); api.Close();
                f.Step(f.Hit(key, device)); Check(f.C.Marbles.Count == 3, "zero-value stationary parent still divides");
                foreach (var child in f.C.Marbles.Keys) { Near(0, f.Simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "zero value remains zero"); Near(0, f.Physics.States[child].LinearVelocity.magnitude, "zero speed remains zero"); }
            }
            SplitterPhysics();
            Debug.Log("SPLITTER_VALIDATION_PASSED: " + (assertions - start) + " assertions; division, multiplier inheritance, lineage, pass-through, atomic rollback, durability, capacity and real PhysX outlets.");
        }
        private static void SplitterPhysics()
        {
            foreach (float dt in new[] { .01f, .02f })
            {
                var scene = SceneManager.CreateScene("SplitterPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var simulation = new MarbleSimulation(Balance()); int contacts = 0;
                var bridge = new MarblePhysicsBridge(scene, null, null, hit => { contacts++; simulation.EnqueueContact(hit); });
                var board = new GameObject("SplitterBoard"); SceneManager.MoveGameObjectToScene(board, scene);
                MResLoadHandle view = null; GameObject obstacle = null;
                try
                {
                    simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                    simulation.ApplyGlobalAttribute(996, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                    var def = c.Balance.Devices.Single(d => d.Id == 9015); CampaignStateUtility.CreateDevice(c, def, def.Price);
                    var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var e);
                    var data = c.Manager.GetComponentData<OwnedDeviceData>(e); data.Placed = true; c.Manager.SetComponentData(e, data);
                    board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(63, 121, 0));
                    view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                    Check(view.Root.GetComponentsInChildren<Collider>().All(x => x.isTrigger), "splitter has no solid collision at any outlet");
                    for (int i = 0; i < 3; i++) Check(view.Root.transform.Find("Outlet" + i) != null, "model contains all three outlets");
                    Check(bridge.TryGetDevicePose(owned.InstanceId, out var pose), "loaded splitter exposes its pose");
                    var down = BallInteractionSystem.Down(pose);
                    var spawn = pose.Position + pose.Up * .12f - down * .85f;
                    void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                    simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                    var key = c.Marbles.Keys.Single();
                    float radius = (float)(c.Content.Definition(GameAttribute.BALL_RADIUS).DefaultValue * Math.Pow(1d / 3, 1d / 3) * AttributeRuntime.RadiusToWorld);
                    var center = pose.Position + pose.Up * (radius + .005f);
                    float distance = pose.Radius + radius + .02f;
                    obstacle = new GameObject("BlockedMiddleOutlet"); SceneManager.MoveGameObjectToScene(obstacle, scene);
                    obstacle.transform.position = center + down * distance; obstacle.AddComponent<SphereCollider>().radius = .015f; Physics.SyncTransforms();
                    Check(bridge.TryMove(key, spawn, down * 4), "approach physical splitter from uphill");
                    for (int n = 0; contacts == 0 && n < 100; n++) Step();
                    Check(contacts > 0 && c.Marbles.ContainsKey(key) && bridge.ActiveBodyCount == 1,
                        $"blocked middle outlet rolls back first physical child (contacts={contacts}, parent={c.Marbles.ContainsKey(key)}, bodies={bridge.ActiveBodyCount}, status={simulation.GetDeviceStatus(owned.InstanceId)})");
                    Near(20, simulation.GetOwnedDevices().Single().RemainingDurability, "blocked physical split is free");
                    UnityEngine.Object.DestroyImmediate(obstacle); obstacle = null;
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "exit trigger before retry"); Step();
                    float entrySpeed = 0;
                    if (dt < .015f)
                    {
                        // Trigger-only contact supports a stationary parent too.
                        Check(bridge.TryMove(key, board.transform.TransformPoint(new Vector3(0, .12f, .06f)), Vector3.zero), "enter splitter stationary"); Step();
                    }
                    else
                    {
                        Check(bridge.TrySetVelocity(key, down * 4), "approach splitter at nonzero speed");
                        for (int n = 0; c.Marbles.ContainsKey(key) && n < 100; n++)
                        {
                            simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt);
                            bridge.TryGetState(key, out var input); entrySpeed = input.LinearVelocity.magnitude;
                            simulation.AfterPhysics(bridge);
                        }
                        Check(entrySpeed > 3.8f, "entry speed includes normal physics damping before contact");
                    }
                    Check(!c.Marbles.ContainsKey(key) && c.Marbles.Count == 3 && bridge.ActiveBodyCount == 3, "physical batch replaces parent at zero or nonzero speed");
                    var children = c.Marbles.Keys.OrderBy(k => k.Sequence).ToArray();
                    for (int i = 0; i < 3; i++)
                    {
                        Check(bridge.TryGetState(children[i], out var state), "child has physical body");
                        VectorNear(center + Quaternion.AngleAxis((i - 1) * 30, pose.Up) * down * distance, state.Position, "physical ports use tilted board plane");
                        VectorNear(Quaternion.AngleAxis((i - 1) * 30, pose.Up) * down * entrySpeed, state.LinearVelocity, "physical split preserves entry speed at three angles");
                    }
                    // Keep one descendant to cross the sensor without hitting its siblings outside the ports.
                    ContentSpawnSystem.Rollback(c, c.Marbles[children[1]]); ContentSpawnSystem.Rollback(c, c.Marbles[children[2]]);
                    var returning = children[0]; Check(bridge.TryMove(returning, spawn, Vector3.zero), "prepare self reentry"); Step();
                    Check(bridge.TrySetVelocity(returning, down * 4), "return toward same instance");
                    for (int n = 0; n < 100; n++) { Step(); bridge.TryGetState(returning, out var state); if (Vector3.Dot(state.Position - pose.Position, down) > .85f) break; }
                    Check(bridge.TryGetState(returning, out var output) && Vector3.Dot(output.Position - pose.Position, down) > .85f, "descendant passes through entire model and trigger");
                    VectorNear(down, output.LinearVelocity.normalized, "returning descendant is neither reflected nor redirected");
                    Near(1, bridge.ActiveBodyCount, "returning descendant never creates more balls");
                    Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "self reentry does not spend another use");
                }
                finally { if (obstacle != null) UnityEngine.Object.DestroyImmediate(obstacle); view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
            }
        }
    }
}
