using System;
using System.Collections.Generic;
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
        private static OwnedDeviceData AddTurf(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9021));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var e);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(e);
        }
        private static void TurfInput(Fixture f, MarbleKey key, OwnedDeviceData device, double value, double multiplier)
        {
            var api = CashApi(f, key, device); api.SetCurrent("BALL_BASE_VALUE", value);
            api.AddScoreMultiplier(multiplier); api.Close();
        }
        private static void TurfLuaApi()
        {
            int start = assertions;
            foreach (var pair in new[] { (2d, 8d), (0d, 0d), (.5d, 2d), (9999d, 1d) })
            using (var f = new Fixture())
            {
                var d = AddTurf(f); var a = f.Launch(); var b = f.Launch();
                TurfInput(f, a, d, pair.Item1, 1.5); TurfInput(f, b, d, pair.Item2, 2.5);
                var aMotion = f.Physics.States[a]; var bMotion = f.Physics.States[b]; double blood = f.C.Player.Blood;
                var score = f.C.Manager.GetComponentData<MarbleScore>(f.Ball(b)); score.ScoreBonus = 9; f.C.Manager.SetComponentData(f.Ball(b), score);
                f.C.Manager.GetBuffer<MarbleModifier>(f.Ball(b)).Add(new MarbleModifier { Attribute = MarbleModifierAttribute.ScoreAdd, Value = 7, Stacks = 1 });
                f.Step(CopyHit(f, a, d), CopyHit(f, a, d));
                Near(2, f.C.Marbles.Count, "first hit only stores a numeric snapshot");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "storage costs no wear");
                var api = CashApi(f, a, d); api.SetCurrent("BALL_BASE_VALUE", 99); api.AddScoreMultiplier(3); api.Close();
                f.Step(CopyHit(f, b, d), CopyHit(f, b, d));
                Near(3, f.C.Marbles.Count, "two distinct balls create one child, retaining both originals");
                var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                Near((pair.Item1 + pair.Item2) / 2, f.Simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "mean uses first impact snapshot and second impact value, preserving fractions");
                Near(2, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(child)).ScoreMultiplier, "device factors average without counting base twice");
                Near(99, f.Simulation.GetMarbleAttribute(a, GameAttribute.BALL_BASE_VALUE), "first original stays unchanged by pairing");
                Near(pair.Item2, f.Simulation.GetMarbleAttribute(b, GameAttribute.BALL_BASE_VALUE), "second original stays unchanged");
                Near(3.5, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "first original multiplier unchanged");
                Near(2.5, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(b)).ScoreMultiplier, "second original multiplier unchanged");
                Near(0, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(child)).ScoreBonus, "child has no inherited fixed bonus");
                Near(0, f.C.Manager.GetBuffer<MarbleModifier>(f.Ball(child)).Length, "child has no inherited flat score modifier");
                VectorNear(aMotion.LinearVelocity, f.Physics.States[a].LinearVelocity, "first solver bounce unchanged");
                VectorNear(bMotion.LinearVelocity, f.Physics.States[b].LinearVelocity, "second solver bounce unchanged");
                VectorNear(Vector3.forward * 6, f.Physics.States[child].LinearVelocity, "horizontal board fallback uses source forward");
                Near(blood, f.C.Player.Blood, "new ball does not charge blood"); Near(0, f.C.Round.Score, "no premature score settlement");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "successful pair uses once");
                api = CashApi(f, child, d); Check(api.HasCopiedFrom(d.InstanceId), "child records source ancestry"); api.Close();
                f.Step(new MarbleContact { Key = child, TargetId = 700, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true });
                Near(pair.Item1 + pair.Item2, f.C.Round.Score + f.C.Round.FractionalScore, "mean value and mean multiplier settle normally");
            }
            TurfPairingBoundaries(); TurfCopyMultiplierApi();
            foreach (float dt in new[] { .01f, .02f }) TurfPhysics(dt);
            Debug.Log("TURF_VALIDATION_PASSED: " + (assertions - start) + " assertions; timed pairing, snapshots, averaged multipliers, rollback, wear, lifecycle and real collisions.");
        }
        private static void TurfPairingBoundaries()
        {
            foreach (double time in new[] { 5.999, 6d, 6.001 })
            using (var f = new Fixture())
            {
                var d = AddTurf(f); var a = f.Launch(); var b = f.Launch();
                SandbagAt(f, 1, CopyHit(f, a, d)); SandbagAt(f, 4, CopyHit(f, a, d));
                Near(2, f.C.Marbles.Count, "same ball cannot pair with itself or refresh the window");
                SandbagAt(f, time, CopyHit(f, b, d));
                Near(time <= 6 ? 3 : 2, f.C.Marbles.Count, "five-second boundary is inclusive and never extended by same-ball hits");
                if (time > 6)
                {
                    SandbagAt(f, 7, CopyHit(f, a, d)); Near(3, f.C.Marbles.Count, "expired window restarts from next valid collision");
                }
            }
            using (var f = new Fixture())
            {
                var d = AddTurf(f); var a = f.Launch(); var b = f.Launch(); var c = f.Launch(); var e = f.Launch();
                var ignored = CopyHit(f, a, d); ignored.LinkKey = 123; f.Step(f.Hit(a, d), ignored);
                f.Step(CopyHit(f, a, d), CopyHit(f, b, d), CopyHit(f, c, d));
                Near(5, f.C.Marbles.Count, "simultaneous contacts form one pair then start a new window");
                var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                f.Step(CopyHit(f, child, d)); Near(5, f.C.Marbles.Count, "own descendants cannot fill the second slot");
                f.Step(CopyHit(f, e, d)); Near(6, f.C.Marbles.Count, "fourth input completes second pair");
                Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "two pairs cost two uses");
                f.Step(CopyHit(f, child, d), CopyHit(f, a, d)); Near(6, f.C.Marbles.Count, "own descendants cannot fill first slot either");
                f.Step(CopyHit(f, b, d)); Near(7, f.C.Marbles.Count, "originals can form another pair after reset");
                var other = AddTurf(f); f.Step(CopyHit(f, child, other)); Near(7, f.C.Marbles.Count, "other turf owns an independent first slot");
                f.Step(CopyHit(f, a, other)); Near(8, f.C.Marbles.Count, "own child may interact with other turf");
                var grandchild = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                // Exercise transitive lineage using the copied child as the second input.
                f.Step(CopyHit(f, a, other)); f.Step(CopyHit(f, child, other));
                grandchild = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                var query = CashApi(f, grandchild, d); Check(query.HasCopiedFrom(d.InstanceId) && query.HasCopiedFrom(other.InstanceId), "inherited copy ancestry remains transitive"); query.Close();
                f.Step(CopyHit(f, grandchild, d)); Near(9, f.C.Marbles.Count, "grandchild cannot start ancestor pairing");
            }
            foreach (bool capacity in new[] { false, true })
            using (var f = new Fixture())
            {
                var d = AddTurf(f); var a = f.Launch(); var b = f.Launch();
                TurfInput(f, a, d, 2, 1); TurfInput(f, b, d, 8, 1); f.Step(CopyHit(f, a, d));
                if (capacity) f.C.Tuning.MaxActiveMarbles = 2; else f.Physics.FailSpawnAt = f.Physics.Attempts + 1;
                f.Step(CopyHit(f, b, d)); Near(2, f.C.Marbles.Count, "failed output rolls back candidate and retains originals");
                Near(2, f.C.Round.ActiveMarbleCount, "failed output preserves active count");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "failure spends no wear");
                f.Physics.FailSpawnAt = 0; f.C.Tuning.MaxActiveMarbles = 20;
                AttributeRuntime.SetBase(f.C, f.Ball(b), GameAttribute.BALL_BASE_VALUE, 10); f.Step(CopyHit(f, b, d));
                var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                Near(6, f.Simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "next collision retries with original first snapshot and current second value");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "successful retry spends only once");
            }
            foreach (int wear in new[] { 1, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9021).Durability = wear;
                var d = AddTurf(f); var a = f.Launch(); var b = f.Launch();
                for (int n = 0; n < 3; n++) { f.Step(CopyHit(f, a, d)); f.Step(CopyHit(f, b, d)); }
                Near(wear == 1 ? 3 : 5, f.C.Marbles.Count, "last use completes fully and infinite wear continues");
                Near(wear == 1 ? 0 : -1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "finite and unlimited wear");
                var state = f.C.Round; state.Phase = (byte)RoundPhase.Build; f.C.Round = state;
                Check(f.Simulation.BeginRound(), "turf remains available next round");
                a = f.Launch(); b = f.Launch(); f.Step(CopyHit(f, a, d)); Near(2, f.C.Marbles.Count, "new round clears first slot");
                f.Step(CopyHit(f, b, d)); Near(3, f.C.Marbles.Count, "new round restores durability");
            }
            using (var f = new Fixture())
            {
                var d = AddTurf(f); var a = f.Launch(); var b = f.Launch();
                TurfInput(f, a, d, 2, 1); f.Step(CopyHit(f, a, d)); ContentSpawnSystem.Rollback(f.C, f.Ball(a));
                f.Step(CopyHit(f, b, d)); Near(2, f.C.Marbles.Count, "first ball disappearing leaves safe numeric snapshot until timeout");
                var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Near(6, f.Simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "snapshot survives first entity removal");
                f.Step(CopyHit(f, b, d)); SandbagTickAt(f, f.C.Round.Time + 5.001);
                Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("等待弹珠"), "timeout clears status without another collision");
                f.Step(CopyHit(f, b, d));
                var state = f.C.Round; state.Phase = (byte)RoundPhase.Build; f.C.Round = state; Check(f.Simulation.BeginRound(), "reset a pending pair at round boundary");
                a = f.Launch(); b = f.Launch(); f.Step(CopyHit(f, b, d)); Near(2, f.C.Marbles.Count, "pending snapshot does not cross rounds");
            }
        }
        private static void TurfCopyMultiplierApi()
        {
            foreach (double factor in new[] { 0d, .5, 1d, 2.25, 1000d })
            using (var f = new Fixture())
            {
                var d = AddTurf(f); var key = f.Launch(); var api = CashApi(f, key, d); api.AddScoreMultiplier(3);
                var copy = api.CreateCopy();
                Check(!copy.TrySetDeviceScoreMultiplier(double.NaN) && !copy.TrySetDeviceScoreMultiplier(double.PositiveInfinity)
                    && !copy.TrySetDeviceScoreMultiplier(-.001) && !copy.TrySetDeviceScoreMultiplier(1000.001), "multiplier override rejects invalid and out-of-range amounts");
                Check(copy.TrySetDeviceScoreMultiplier(factor), "copy accepts complete replacement device factor");
                Check(copy.TrySpawn(Vector3.one * 3, Vector3.zero), "copy generates with configured multiplier");
                Check(!copy.TrySetDeviceScoreMultiplier(1), "submitted request cannot mutate its child");
                var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                Near(factor, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(child)).ScoreMultiplier, "replacement is amount minus one, including zero");
                Near(3, api.DeviceScoreMultiplier, "replacement never changes parent's multiplier");
                Near(10, f.Simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "multiplier override does not change copied value");
                Near(20, api.RemainingDurability, "multiplier/copy primitives do not implicitly consume wear");
                api.Close(); ExpectExpired(() => copy.TrySetDeviceScoreMultiplier(1));
            }
        }
        private static void TurfPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("TurfPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("TurfBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            GameObject blocker = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 1);
                simulation.ApplyGlobalAttribute(995, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var def = c.Balance.Devices.Single(d => d.Id == 9021); CampaignStateUtility.CreateDevice(c, def, def.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>(); Check(collider != null && collider.convex && !collider.isTrigger, "turf has solid collision surface");
                Check(bridge.TryGetDevicePose(owned.InstanceId, out var pose), "physical turf pose available");
                var down = BallInteractionSystem.Down(pose).normalized;
                var spawn = pose.Position + pose.Up * .1f - down * .8f; var incoming = down * 2;
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                MarbleBody Body(MarbleKey key) => scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key));
                MarbleKey Launch(double value, double factor)
                {
                    simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                    var key = c.Marbles.Keys.OrderBy(k => k.Sequence).Last(); var body = Body(key).Body; body.drag = body.angularDrag = 0;
                    AttributeRuntime.SetBase(c, c.Marbles[key], GameAttribute.BALL_BASE_VALUE, value);
                    var score = c.Manager.GetComponentData<MarbleScore>(c.Marbles[key]); score.AddDeviceMultiplier(factor); c.Manager.SetComponentData(c.Marbles[key], score);
                    Check(bridge.TrySetVelocity(key, incoming), "launch input at real turf"); return key;
                }
                void Hit(MarbleKey key)
                {
                    int count = hits.Count(h => h.Key.Equals(key));
                    for (int n = 0; hits.Count(h => h.Key.Equals(key)) == count && n < 100; n++) Step();
                    Check(hits.Count(h => h.Key.Equals(key)) == count + 1, "one real collision enters");
                }
                var a = Launch(4, 2); Hit(a);
                Near(1, c.Marbles.Count, "physical first ball only arms turf"); Near(20, simulation.GetOwnedDevices().Single().RemainingDurability, "first collision is free");
                Check(Vector3.Dot(Body(a).Body.velocity, incoming) < 0, "first ball bounces normally");
                Check(bridge.TryMove(a, board.transform.TransformPoint(new Vector3(2, .1f, 0)), Vector3.zero), "park first input clear of output");
                blocker = new GameObject("BlockedTurfOutput"); SceneManager.MoveGameObjectToScene(blocker, scene);
                blocker.transform.SetPositionAndRotation(pose.Position + down * .38f + pose.Up * .1f, Quaternion.LookRotation(down, pose.Up));
                blocker.AddComponent<BoxCollider>().size = new Vector3(.3f, .3f, .2f); Physics.SyncTransforms();
                var b = Launch(10, 4); Hit(b);
                Near(2, c.Marbles.Count, "blocked physical outlet retains originals"); Near(20, simulation.GetOwnedDevices().Single().RemainingDurability, "blocked output costs no wear");
                Check(Vector3.Dot(Body(b).Body.velocity, incoming) < 0, "second ball still bounces when outlet blocked");
                UnityEngine.Object.DestroyImmediate(blocker); blocker = null; Physics.SyncTransforms();
                Check(bridge.TryMove(b, spawn, Vector3.zero), "leave contact before retry"); Step();
                bridge.TrySetVelocity(b, incoming); Hit(b);
                Near(3, c.Marbles.Count, "real reentry generates one non-overlapping child"); Near(3, bridge.ActiveBodyCount, "child has live physics body");
                var child = c.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Check(bridge.TryGetState(child, out var output), "read physical child");
                VectorNear(Vector3.ProjectOnPlane(Vector3.down, board.transform.up).normalized * 6, output.LinearVelocity, "child launches downhill on tilted board");
                Near(7, simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "physical child mean value");
                Near(3, c.Manager.GetComponentData<MarbleScore>(c.Marbles[child]).ScoreMultiplier, "physical child mean device factor");
                Near(4, simulation.GetMarbleAttribute(a, GameAttribute.BALL_BASE_VALUE), "first physical original retained");
                Near(10, simulation.GetMarbleAttribute(b, GameAttribute.BALL_BASE_VALUE), "second physical original retained");
                Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "physical success charges one use");
                Check(Vector3.Dot(Body(b).Body.velocity, incoming) < 0, "successful generation leaves solver bounce intact");
                ContentSpawnSystem.Rollback(c, c.Marbles[a]); ContentSpawnSystem.Rollback(c, c.Marbles[b]);
                bridge.TryMove(child, spawn, Vector3.zero); Step(); bridge.TrySetVelocity(child, incoming); Hit(child);
                Near(1, c.Marbles.Count, "own descendant still collides but cannot arm turf");
                bridge.TryMove(child, board.transform.TransformPoint(new Vector3(2, .1f, 0)), Vector3.zero);
                var fresh = Launch(6, 1); Hit(fresh); Near(2, c.Marbles.Count, "fresh ball starts first slot after ignored descendant");
                Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "descendant and new first slot cost no wear");
            }
            finally { if (blocker != null) UnityEngine.Object.DestroyImmediate(blocker); view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
