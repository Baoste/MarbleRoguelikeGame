using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static OwnedDeviceData AddBomb(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9017));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var e);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(e);
        }
        private static void BombUntil(Fixture f, double time)
        { while (f.C.Round.Time + 1e-6 < time) f.Step(); }
        private static Vector3 BombDestination(Fixture f, OwnedDeviceData device)
        {
            var flight = f.C.Transients.Items.Single(v => v.NodePath == "BombTemplate" && v.Source == device.InstanceId);
            return flight.PositionAt(flight.StartedAt + flight.Duration);
        }
        private static void BombLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9017).Durability = 1;
                var device = AddBomb(f); var first = f.Launch(); var second = f.Launch(); var outside = f.Launch();
                f.C.Tuning.MaxActiveMarbles = 17; // Exact final capacity: sixteen fragments and the untouched outside ball.
                var api = CashApi(f, first, device); api.AddScoreMultiplier(1.5); api.AddScoreMultiplier(1.5); api.Close();
                BombUntil(f, 5); Near(0, f.C.Transients.Count, "no throw before first five-second boundary");
                f.Step(); Near(1, f.C.Transients.Count, "first throw occurs at five seconds without an incoming contact");
                Near(1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "throw is free until explosion");
                var center = BombDestination(f, device);
                f.Physics.TryMove(first, center + Vector3.right * .3f, Vector3.zero);
                f.Physics.TryMove(second, center - Vector3.right * .3f, Vector3.zero);
                f.Physics.TryMove(outside, center + Vector3.forward * 2, Vector3.zero);
                BombUntil(f, 5.9); Check(f.C.Marbles.ContainsKey(first), "fuse lasts one full second");
                f.Step();
                Check(!f.C.Marbles.ContainsKey(first) && !f.C.Marbles.ContainsKey(second) && f.C.Marbles.ContainsKey(outside), "blast splits nearby parents only");
                var children = f.C.Marbles.Keys.Where(k => !k.Equals(outside)).OrderBy(k => k.Sequence).ToArray();
                Near(16, children.Length, "last use affects every parent; new children do not reenter query");
                Near(0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "whole explosion costs one durability");
                for (int i = 0; i < children.Length; i++)
                {
                    var k = children[i]; Near(1.25, f.Simulation.GetMarbleAttribute(k, GameAttribute.BALL_BASE_VALUE), "one eighth value");
                    Near(.25, f.Simulation.GetMarbleAttribute(k, GameAttribute.BALL_RADIUS), "one eighth volume");
                    Near(.125, f.Simulation.GetMarbleAttribute(k, GameAttribute.BALL_MASS), "one eighth mass");
                    Near(6, f.Physics.States[k].LinearVelocity.magnitude, "blast gives configured scatter speed");
                    Near(i < 8 ? 2.25 : 1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(k)).ScoreMultiplier, "each parent's own multiplier inherited");
                    api = CashApi(f, k, device); Check(api.HasSplitFrom(device.InstanceId), "blast ancestry recorded"); api.Close();
                }
                BombUntil(f, 6.3); Near(0, f.C.Transients.Count, "flash removed after final use");
                f.Step(children.Select(k => new MarbleContact { Key = k, TargetId = 709, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true }).ToArray());
                Near(32.5, f.C.Round.Score + f.C.Round.FractionalScore, "two original values and independent factors conserved at scoring");
                BombUntil(f, 11.1); Near(0, f.C.Transients.Count, "depleted launcher throws no more bombs");
            }
            foreach (int wear in new[] { 1, 20, -1 })
                using (var f = new Fixture())
                {
                    f.C.Balance.Devices.Single(d => d.Id == 9017).Durability = wear; AddBomb(f);
                    BombUntil(f, 6); Near(wear == -1 ? -1 : wear - 1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "empty blast also spends one");
                    BombUntil(f, 106.3); Near(wear == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "empty blasts exhaust finite wear; unlimited stays unlimited");
                    Near(0, f.C.Marbles.Count, "visual bombs and flashes are never scoring marbles");
                }
            foreach (bool capacity in new[] { false, true })
                using (var f = new Fixture())
                {
                    var device = AddBomb(f); var key = f.Launch(); BombUntil(f, 5.1);
                    f.Physics.TryMove(key, BombDestination(f, device), Vector3.zero);
                    if (capacity) f.C.Tuning.MaxActiveMarbles = 7; else f.Physics.FailSpawnAt = f.Physics.Attempts + 8;
                    BombUntil(f, 6); Check(f.C.Marbles.ContainsKey(key) && f.C.Marbles.Count == 1, "failed eight-way split keeps complete parent");
                    Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "no value lost on rejection");
                    Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "failed fragmentation still counts as explosion");
                }
            using (var f = new Fixture())
            {
                var device = AddBomb(f); var key = f.Launch(); BombUntil(f, 5.1); var firstCenter = BombDestination(f, device);
                f.Physics.TryMove(key, firstCenter, Vector3.zero); BombUntil(f, 6);
                var children = f.C.Marbles.Keys.ToArray(); BombUntil(f, 10.1); var nextCenter = BombDestination(f, device);
                Check((nextCenter - firstCenter).sqrMagnitude > .01f, "seeded random direction varies on subsequent throws");
                foreach (var k in children) f.Physics.TryMove(k, nextCenter, Vector3.zero);
                BombUntil(f, 11); Near(8, f.C.Marbles.Count, "same launcher's descendants are not fragmented again");
                Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "ancestor-only explosion still spends one use");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "new bomb round begins");
                Near(0, f.C.Transients.Count, "new round clears visual effects"); BombUntil(f, 5); Near(0, f.C.Transients.Count, "new round restarts full throw interval");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "new round restores durability");
            }
            foreach (bool endRound in new[] { false, true })
                using (var f = new Fixture())
                {
                    var device = AddBomb(f); BombUntil(f, 5.1);
                    if (endRound) f.C.BeginDraining();
                    else { CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var e); var data = f.C.Manager.GetComponentData<OwnedDeviceData>(e); data.Placed = false; f.C.Manager.SetComponentData(e, data); }
                    f.Step(); Near(0, f.C.Transients.Count, "unplaced source or completed round cancels in-flight visuals");
                    Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "cancellation is not an explosion");
                }
            TransientApiValidation(); BombPhysics();
            Debug.Log("BOMB_VALIDATION_PASSED: " + (assertions - start) + " assertions; cadence, fuse, empty wear, area snapshot, splitting, random flight, lifecycle and physical visuals.");
        }
        private static void TransientApiValidation()
        {
            using (var f = new Fixture())
            {
                var device = AddBomb(f); CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var entity); f.Step();
                var api = new DeviceEventContext(f.C, entity); var random = f.C.Round.RandomState;
                Near(3, api.RandomRange(3, 3), "equal random bounds return exact value"); Check(random == f.C.Round.RandomState, "equal bounds do not advance RNG");
                ExpectArgument(() => api.RandomRange(2, 1)); ExpectArgument(() => api.ForEachBallInRadius(Vector3.zero, float.NaN, "Missing"));
                Near(0, api.TrySpawnVisual("x", Vector3.zero, Vector3.one, 0, ""), "zero lifetime rejected");
                int id = api.TryScheduleTransient(Vector3.one, Vector3.right, .2f, "Missing", null);
                Check(id > 0, "a timer can run without a presentation template");
                api.Close(); ExpectExpired(() => api.TrySpawnVisual("x", Vector3.zero, Vector3.zero, 1, ""));
                f.Step(); VectorNear(Vector3.one + Vector3.right * .1f,
                    f.C.Transients.Items.Single(item => item.Id == id).PositionAt(f.C.Round.Time), "trajectory uses simulation time");
                f.Step(); Near(0, f.C.Transients.Count, "missing callback still cleans visual");
                var resource = DeviceResourceCatalog.Current.Get(9017);
                var probe = resource.CreateScript(@"local saved, info, calls = nil, nil, 0
function Done(a) saved, info, calls = a, a.Transient, calls+1; if calls==1 then error('EXPECTED_TRANSIENT_FAILURE') end end
function Verify() assert(calls==2); assert(not pcall(function() return saved.Time end)); assert(not pcall(function() return info.Position end)) end");
                var scripts = (Dictionary<int, DeviceLuaScript>)typeof(DeviceScriptSystem).GetField("scripts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.C.DeviceScripts);
                f.C.DeviceScripts.Remove(device.InstanceId); scripts.Add(device.InstanceId, probe);
                api = new DeviceEventContext(f.C, entity);
                api.TrySpawnVisual("x", Vector3.zero, Vector3.zero, .1f, "Done"); api.TrySpawnVisual("x", Vector3.one, Vector3.zero, .1f, "Done"); api.Close();
                f.Step(); Near(0, f.C.Transients.Count, "throwing completion does not strand another visual"); probe.Invoke("Verify");
                api = new DeviceEventContext(f.C, entity); api.TrySpawnVisual("x", Vector3.zero, Vector3.zero, 10, "Done"); api.Close();
                f.Simulation.Dispose(); Near(0, f.C.Transients.Count, "dispose cancels pending callbacks");
            }
        }
        private static void BombPhysics()
        {
            foreach (float dt in new[] { .01f, .02f })
            {
                var scene = SceneManager.CreateScene("BombPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var simulation = new MarbleSimulation(Balance()); var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact);
                using var presentation = new DevicePresentation(bridge);
                var board = new GameObject("BombBoard"); SceneManager.MoveGameObjectToScene(board, scene); DevicePhysicsFixture view = null;
                try
                {
                    simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                    simulation.ApplyGlobalAttribute(999, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                    var def = c.Balance.Devices.Single(d => d.Id == 9017); def.Durability = 1;
                    CampaignStateUtility.CreateDevice(c, def, def.InstallPrice); var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var e);
                    var data = c.Manager.GetComponentData<OwnedDeviceData>(e); data.Placed = true; c.Manager.SetComponentData(e, data);
                    board.transform.SetPositionAndRotation(new Vector3(0, 8, 0), Quaternion.Euler(-63, 121, 0));
                    view = DevicePhysicsFixture.Place(simulation, owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                    presentation.Sync(c);
                    var model = presentation.GetModel(owned.InstanceId).transform;
                    Check(!model.Find("BombTemplate").gameObject.activeSelf && !model.Find("BlastTemplate").gameObject.activeSelf, "templates hidden on original launcher");
                    var spawn = board.transform.TransformPoint(new Vector3(0, .12f, -2));
                    void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); presentation.Sync(c); }
                    simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                    var key = c.Marbles.Keys.Single(); bridge.TryMove(key, spawn, Vector3.zero);
                    while (c.Transients.Count == 0 && c.Round.Time < 5 + dt * 3) Step();
                    Check(c.Transients.Count == 1 && c.Round.Time >= 5, "first physical throw occurs on the next fixed-step boundary after five seconds");
                    double thrownAt = c.Round.Time - dt;
                    var visual = view.Root.GetComponentsInChildren<Transform>().Single(t => t.name.StartsWith("TransientVisual ", StringComparison.Ordinal));
                    Check(visual.GetComponentsInChildren<Collider>().Length == 0 && visual.GetComponentsInChildren<Rigidbody>().Length == 0, "flying visual has no physics and no marble identity");
                    Check(visual.gameObject.scene == scene, "transient stays in source physics scene");
                    Near(1, bridge.ActiveBodyCount, "projectile is not a scoring marble");
                    bridge.TryGetDevicePose(owned.InstanceId, out var pose);
                    var direction = Vector3.ProjectOnPlane(visual.position - pose.Position, pose.Up).normalized;
                    var destination = pose.Position + pose.Up * .12f + direction * (pose.Radius + .12f + 3);
                    var before = visual.position; Step(); Near(3 * dt, Vector3.Distance(before, visual.position), "real visual flight has speed three");
                    Check(bridge.TryMove(key, destination, Vector3.zero), "put target into future blast center");
                    while (c.Marbles.ContainsKey(key) && c.Round.Time < thrownAt + 1 + dt * 2) Step();
                    Check(c.Round.Time >= thrownAt + 1 - 1e-6 && c.Round.Time <= thrownAt + 1 + dt + 1e-6, "physical fuse completes at the first boundary after one second");
                    Check(!c.Marbles.ContainsKey(key) && c.Marbles.Count == 8 && bridge.ActiveBodyCount == 8, "actual timed projectile explodes into eight physical balls");
                    foreach (var k in c.Marbles.Keys)
                    {
                        bridge.TryGetState(k, out var state); Near(6, state.LinearVelocity.magnitude, "real fragment speed six");
                        Near(0, Vector3.Dot(state.LinearVelocity, pose.Up), "fragments stay in tilted board plane");
                        Near(1.25, simulation.GetMarbleAttribute(k, GameAttribute.BALL_BASE_VALUE), "physical fragments retain total value");
                    }
                    Near(0, simulation.GetOwnedDevices().Single().RemainingDurability, "final explosion spent exactly one use");
                    while (c.Round.Time < 6.3) Step();
                    Check(!view.Root.GetComponentsInChildren<Transform>().Any(t => t.name.StartsWith("TransientVisual ", StringComparison.Ordinal)), "final flash disappears without requiring another throw");
                }
                finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
            }
        }
    }
}
