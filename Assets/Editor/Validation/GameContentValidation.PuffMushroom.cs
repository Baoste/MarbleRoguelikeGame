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
        private static OwnedDeviceData AddPuffMushroom(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9016));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var e);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(e);
        }
        private static void PuffMushroomLuaApi()
        {
            int start = assertions;
            foreach (int wear in new[] { 1, 20, -1 })
                using (var f = new Fixture())
                {
                    f.C.Balance.Devices.Single(d => d.Id == 9016).Durability = wear;
                    var device = AddPuffMushroom(f); var key = f.Launch(); var api = CashApi(f, key, device);
                    api.AddScoreMultiplier(1.5); api.AddScoreMultiplier(1.5); api.Close();
                    float speed = f.Physics.States[key].LinearVelocity.magnitude; double blood = f.C.Player.Blood;
                    f.Step(f.Hit(key, device), f.Hit(key, device));
                    Check(!f.C.Marbles.ContainsKey(key) && f.C.Marbles.Count == 8, "mushroom replaces parent with exactly eight children, deduplicating entry reports");
                    Near(blood, f.C.Player.Blood, "eight-way split neither charges nor refunds blood"); Near(0, f.C.Round.Score, "split does not score immediately");
                    var keys = f.C.Marbles.Keys.OrderBy(k => k.Sequence).ToArray();
                    for (int i = 0; i < 8; i++)
                    {
                        Near(1.25, f.Simulation.GetMarbleAttribute(keys[i], GameAttribute.BALL_BASE_VALUE), "eighth of value");
                        Near(.25, f.Simulation.GetMarbleAttribute(keys[i], GameAttribute.BALL_RADIUS), "eighth of volume halves radius");
                        Near(.125, f.Simulation.GetMarbleAttribute(keys[i], GameAttribute.BALL_MASS), "eighth of mass");
                        Near(2, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(keys[i])).ScoreMultiplier, "accumulated multiplier inherited without division");
                        VectorNear(Quaternion.AngleAxis(-60 + i * (120f / 7), Vector3.up) * Vector3.back * speed, f.Physics.States[keys[i]].LinearVelocity, "eight evenly spaced uphill directions retain input speed");
                        var query = CashApi(f, keys[i], device); Check(query.HasSplitFrom(device.InstanceId), "mushroom source recorded"); query.Close();
                    }
                    f.Step(keys.Select(k => f.Hit(k, device)).ToArray());
                    Near(8, f.C.Marbles.Count, "same source is transparent to every child");
                    Near(wear == -1 ? -1 : wear - 1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "whole batch costs one use including final use; reentry costs none");
                    f.Step(keys.Select(k => new MarbleContact { Key = k, TargetId = 702, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true }).ToArray());
                    Near(20, f.C.Round.Score, "all eight shares settle to original value times accumulated factor");
                }
            foreach (int failedChild in new[] { 1, 4, 8 })
                using (var f = new Fixture())
                {
                    var device = AddPuffMushroom(f); var key = f.Launch(); var before = f.Physics.States[key];
                    f.Physics.FailSpawnAt = f.Physics.Attempts + failedChild; f.Step(f.Hit(key, device));
                    Check(f.C.Marbles.Count == 1 && f.C.Marbles.ContainsKey(key) && f.Physics.Spawns.Count == 1, "any failed outlet rolls back all preceding children");
                    Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "failure does not spend durability");
                    Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "failure retains entire original value");
                    VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "failed batch retains motion");
                    f.Physics.FailSpawnAt = 0; f.Step(f.Hit(key, device)); Near(8, f.C.Marbles.Count, "reentry retries failed batch");
                }
            foreach (int capacity in new[] { 7, 8 })
                using (var f = new Fixture())
                {
                    var device = AddPuffMushroom(f); var key = f.Launch(); f.C.Tuning.MaxActiveMarbles = capacity;
                    f.Step(f.Hit(key, device)); Near(capacity == 8 ? 8 : 1, f.C.Round.ActiveMarbleCount, "eight-way capacity uses net replacement count");
                    Near(capacity == 8 ? 19 : 20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "capacity rejection is free");
                }
            using (var f = new Fixture())
            {
                var a = AddPuffMushroom(f); var b = AddPuffMushroom(f); var key = f.Launch();
                var api = CashApi(f, key, a); api.Set("BALL_MASS", 8); api.Close(); // Enough mass for two eight-way generations.
                f.Step(f.Hit(key, a)); var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).First();
                f.Step(f.Hit(child, b)); Near(15, f.C.Marbles.Count, "another mushroom instance can divide a child when attributes permit");
                var descendants = f.C.Marbles.Keys.Where(k => f.C.Manager.GetComponentData<BallFeatures>(f.Ball(k)).Generation == 2).ToArray();
                foreach (var k in descendants)
                {
                    api = CashApi(f, k, a); Check(api.HasSplitFrom(a.InstanceId) && api.HasSplitFrom(b.InstanceId), "mushroom grandchildren preserve both ancestors"); api.Close();
                }
                f.Step(descendants.Select(k => f.Hit(k, a)).ToArray()); Near(15, f.C.Marbles.Count, "grandchildren pass original mushroom");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == a.InstanceId).RemainingDurability, "grandchildren spend no source durability");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "mushroom resets for another round");
                Near(20, f.Simulation.GetOwnedDevices().First().RemainingDurability, "round restores wear");
                var fresh = f.Launch(); f.Step(f.Hit(fresh, a)); Near(8, f.C.Marbles.Count, "fresh round accepts a fresh ball");
            }
            foreach (float angle in new[] { 0, 30, 90, 91 })
                using (var f = new Fixture())
                {
                    f.C.Balance.Devices.Single(d => d.Id == 9016).Range = angle;
                    var device = AddPuffMushroom(f); var key = f.Launch(); f.Step(f.Hit(key, device));
                    bool valid = angle >= 1 && angle <= 90;
                    Near(valid ? 8 : 1, f.C.Marbles.Count, "configured half fan angle accepts 1..90 degrees only");
                    if (valid)
                    {
                        var first = f.C.Marbles.Keys.OrderBy(k => k.Sequence).First();
                        VectorNear(Quaternion.AngleAxis(-angle, Vector3.up) * Vector3.back, f.Physics.States[first].LinearVelocity.normalized, "Range denotes half fan span");
                    }
                }
            using (var f = new Fixture())
            {
                var device = AddPuffMushroom(f); var key = f.Launch(); var api = CashApi(f, key, device);
                api.Set("BALL_MASS", .5); api.Close(); f.Step(f.Hit(key, device));
                Check(f.C.Marbles.ContainsKey(key), "too-small parent keeps original ball instead of inflating child mass");
                api = CashApi(f, key, device); api.Set("BALL_MASS", 1); api.Set("BALL_BASE_VALUE", 0); api.Motion.TrySetVelocity(Vector3.zero); api.Close();
                f.Step(f.Hit(key, device)); Near(8, f.C.Marbles.Count, "stationary zero-value ball still splits");
                foreach (var k in f.C.Marbles.Keys) { Near(0, f.Physics.States[k].LinearVelocity.magnitude, "stationary output remains stationary"); Near(0, f.Simulation.GetMarbleAttribute(k, GameAttribute.BALL_BASE_VALUE), "zero value is conserved"); }
            }
            PuffMushroomPhysics();
            Debug.Log("PUFF_MUSHROOM_VALIDATION_PASSED: " + (assertions - start) + " assertions; eight-way uphill fan, conservation, score inheritance, atomic failure, source ancestry, durability and real PhysX.");
        }
        private static void PuffMushroomPhysics()
        {
            foreach (float dt in new[] { .01f, .02f })
            {
                var scene = SceneManager.CreateScene("PuffMushroomPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var simulation = new MarbleSimulation(Balance());
                var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact);
                var board = new GameObject("PuffMushroomBoard"); SceneManager.MoveGameObjectToScene(board, scene);
                MResLoadHandle view = null; GameObject obstacle = null;
                try
                {
                    simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                    simulation.ApplyGlobalAttribute(997, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                    var def = c.Balance.Devices.Single(d => d.Id == 9016); CampaignStateUtility.CreateDevice(c, def, def.Price);
                    var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var e);
                    var data = c.Manager.GetComponentData<OwnedDeviceData>(e); data.Placed = true; c.Manager.SetComponentData(e, data);
                    board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(-63, 121, 0));
                    view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                    var colliders = view.Root.GetComponentsInChildren<Collider>().Where(c => c.name != "PlacementVolume").ToArray(); Check(colliders.Length == 1 && colliders[0].isTrigger, "mushroom has a single nonsolid entry sensor");
                    Check(view.Root.transform.Find("Stem") != null && view.Root.transform.Find("Cap") != null, "package contains mushroom model");
                    for (int i = 0; i < 8; i++) Check(view.Root.transform.Find("Opening" + i) != null, "model contains eight open mouths");
                    Check(bridge.TryGetDevicePose(owned.InstanceId, out var pose), "loaded mushroom has physics pose");
                    var uphill = -BallInteractionSystem.Down(pose); var spawn = pose.Position + pose.Up * .12f - uphill * .9f;
                    void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                    simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                    var key = c.Marbles.Keys.Single(); const float radius = .04f; float distance = pose.Radius + radius + .02f;
                    var center = pose.Position + pose.Up * (radius + .005f);
                    obstacle = new GameObject("BlockedFourthMouth"); SceneManager.MoveGameObjectToScene(obstacle, scene);
                    obstacle.transform.position = center + Quaternion.AngleAxis(-60 + 3 * (120f / 7), pose.Up) * uphill * distance;
                    obstacle.AddComponent<SphereCollider>().radius = .015f; Physics.SyncTransforms();
                    Check(bridge.TryMove(key, pose.Position + pose.Up * .12f, Vector3.zero), "place ball inside mushroom sensor"); Step();
                    Check(c.Marbles.ContainsKey(key) && c.Marbles.Count == 1 && bridge.ActiveBodyCount == 1, "fourth blocked mouth rolls back all physical children");
                    Near(20, simulation.GetOwnedDevices().Single().RemainingDurability, "physical obstruction is free");
                    UnityEngine.Object.DestroyImmediate(obstacle); obstacle = null;
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "exit before reentry"); Step();
                    float speed = 0;
                    if (dt < .015f) { Check(bridge.TryMove(key, pose.Position + pose.Up * .12f, Vector3.zero), "zero-speed entry"); Step(); }
                    else
                    {
                        Check(bridge.TrySetVelocity(key, uphill * 4), "approach mushroom moving uphill");
                        for (int n = 0; c.Marbles.ContainsKey(key) && n < 100; n++)
                        {
                            simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt);
                            bridge.TryGetState(key, out var input); speed = input.LinearVelocity.magnitude; simulation.AfterPhysics(bridge);
                        }
                        Check(speed > 3.8f, "input retains normal approach speed");
                    }
                    Check(!c.Marbles.ContainsKey(key) && c.Marbles.Count == 8 && bridge.ActiveBodyCount == 8, "all eight physical outputs replace parent");
                    var children = c.Marbles.Keys.OrderBy(k => k.Sequence).ToArray();
                    for (int i = 0; i < 8; i++)
                    {
                        bridge.TryGetState(children[i], out var state); var direction = Quaternion.AngleAxis(-60 + i * (120f / 7), pose.Up) * uphill;
                        VectorNear(center + direction * distance, state.Position, "eight mouth positions align with tilted board");
                        VectorNear(direction * speed, state.LinearVelocity, "uphill fan preserves real entry speed");
                        Check(Vector3.Dot(direction, uphill) >= .4999f, "every output points into upper 120 degree fan");
                    }
                    for (int i = 1; i < 8; i++) ContentSpawnSystem.Rollback(c, c.Marbles[children[i]]);
                    var returning = children[0]; Check(bridge.TryMove(returning, spawn, Vector3.zero), "prepare descendant reentry"); Step();
                    Check(bridge.TrySetVelocity(returning, uphill * 4), "descendant approaches original mushroom");
                    for (int n = 0; n < 130; n++) { Step(); bridge.TryGetState(returning, out var state); if (Vector3.Dot(state.Position - pose.Position, uphill) > .9f) break; }
                    Check(bridge.TryGetState(returning, out var output) && Vector3.Dot(output.Position - pose.Position, uphill) > .9f, "descendant passes straight through mushroom and mouths");
                    VectorNear(uphill, output.LinearVelocity.normalized, "self return never redirects or reflects");
                    Near(1, bridge.ActiveBodyCount, "self return creates no more children"); Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "self return is free");
                }
                finally { if (obstacle != null) UnityEngine.Object.DestroyImmediate(obstacle); view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
            }
        }
    }
}
