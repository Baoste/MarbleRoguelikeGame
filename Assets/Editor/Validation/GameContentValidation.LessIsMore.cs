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
        private static OwnedDeviceData AddLessIsMore(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9024));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void LessIsMoreLuaApi()
        {
            int start = assertions;
            foreach (int count in new[] { 0, 1, 2, 3, 9, 10, 11, 20 })
            using (var f = new Fixture())
            {
                var d = AddLessIsMore(f); var key = f.Launch();
                for (int n = 0; n < count; n++) MoreCompanion(f.C, true, n % 2 == 0);
                for (int n = 0; n < 3; n++) MoreCompanion(f.C, false);
                var before = f.Physics.States[key]; double blood = f.C.Player.Blood, coins = f.C.Campaign.Coins;
                var hit = PiggyHit(f, key, d); f.Step(hit, hit);
                double bonus = Math.Max(0, 20 - count * 2);
                Near(1 + bonus, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "inverse count: zero companions gives plus twenty, ten or more gives zero");
                Near(bonus == 0 ? 20 : 19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "same tick duplicate spends once, zero bonus is free");
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "less is more leaves base value intact");
                VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "normal bounce velocity is preserved");
                Near(blood, f.C.Player.Blood, "count bonus does not spend blood"); Near(coins, f.C.Campaign.Coins, "count bonus does not spend coins");
                Near(0, f.C.Round.Score, "bonus waits for score area");
                Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("其他装置 " + count + " · 附加倍率 +" + bonus), "display reflects current count and nonnegative bonus");
                var flags = f.C.Manager.GetComponentData<BallFeatures>(f.Ball(key));
                Near(bonus == 0 ? 0 : d.InstanceId, flags.FirstTriggeredDeviceId, "only a positive bonus records a successful device effect");
                f.Step(hit); Near(1 + 2 * bonus, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "repeat contacts add again; twenty is a per-hit cap");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(10 * (1 + 2 * bonus), f.C.Round.Score + f.C.Round.FractionalScore, "score area applies accumulated factor once");
            }
            LessIsMoreLiveLayout(); LessIsMoreLimits();
            foreach (float dt in new[] { .01f, .02f }) LessIsMorePhysics(dt);
            Debug.Log("LESS_IS_MORE_VALIDATION_PASSED: " + (assertions - start) + " assertions; inverse counts, live placement, zero boundary, additive scoring, durability and real impacts.");
        }
        private static void LessIsMoreLiveLayout()
        {
            using (var f = new Fixture())
            {
                var d = AddLessIsMore(f); var a = f.Launch(); var b = f.Launch();
                var other = MoreCompanion(f.C, false);
                f.Step(PiggyHit(f, a, d)); Near(21, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "unplaced inventory does not reduce maximum bonus");
                MorePlace(f.C, other, true); f.Step();
                Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("+18"), "placement refreshes displayed bonus without an impact");
                Near(21, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "new placement does not remove previously earned bonus");
                f.Step(PiggyHit(f, a, d), PiggyHit(f, b, d));
                Near(39, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "first ball adds current bonus");
                Near(19, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(b)).ScoreMultiplier, "second simultaneous ball acts independently");
                Near(17, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "three effective collisions cost three uses");
                MorePlace(f.C, other, false); f.Step(PiggyHit(f, a, d));
                Near(59, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "retrieval restores the maximum for future impacts");
                MorePlace(f.C, other, true); var exhausted = MoreCompanion(f.C, true, true);
                f.Step(PiggyHit(f, a, d)); Near(75, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "exhausted placed device still reduces bonus");
                CampaignStateUtility.FindDevice(f.C, exhausted, out var removed); f.C.Manager.DestroyEntity(removed);
                f.Step(PiggyHit(f, a, d)); Near(93, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "deleted device stops reducing subsequent bonus");
                var ignored = PiggyHit(f, a, d); ignored.LinkKey = 88; f.Step(f.Hit(a, d), ignored);
                Near(14, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "trigger-only and link events do not act");
                var second = AddLessIsMore(f); f.Step(PiggyHit(f, a, second));
                Near(109, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "another instance counts first device and companion, excluding itself");
                Near(19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == second.InstanceId).RemainingDurability, "second instance has independent durability");
                var query = CashApi(f, a, d); Check(query.HasTriggeredOtherDevice, "second successful instance records effect history"); query.Close();
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "begin new round with existing placement");
                var fresh = f.Launch(); f.Step(PiggyHit(f, fresh, d));
                Near(17, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(fresh)).ScoreMultiplier, "new round counts current companions without stale ball bonus");
                Near(19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "round boundary resets durability");
            }
            using (var f = new Fixture())
            {
                var d = AddLessIsMore(f); var key = f.Launch(); MoreCompanion(f.C, true);
                var api = CashApi(f, key, d); api.SetDeviceScoreMultiplier(0); api.Close();
                f.Step(PiggyHit(f, key, d)); Near(18, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "bonus can restore a previously zeroed multiplier");
            }
        }
        private static void LessIsMoreLimits()
        {
            foreach (int wear in new[] { 1, 20, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9024).Durability = wear;
                var d = AddLessIsMore(f); var key = f.Launch();
                for (int n = 0; n < 22; n++) f.Step(PiggyHit(f, key, d));
                int uses = wear == -1 ? 22 : wear;
                Near(1 + 20 * uses, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "last use completes fully, unlimited durability continues");
                Near(wear == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "finite and unlimited wear limits");
            }
            foreach (var config in new[] { (0d, 2f, 0d), (20d, 0f, 20d), (5.5d, .75f, 2.5d), (5.5d, 2f, 0d) })
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 9024); definition.Value = config.Item1; definition.Strength = config.Item2;
                var d = AddLessIsMore(f); var key = f.Launch(); for (int n = 0; n < 4; n++) MoreCompanion(f.C, true);
                f.Step(PiggyHit(f, key, d));
                Near(1 + config.Item3, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "configurable cap and reduction support zero and fractions");
                Near(config.Item3 == 0 ? 20 : 19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "only positive configured bonus consumes durability");
            }
        }
        private static void LessIsMorePhysics(float dt)
        {
            var scene = SceneManager.CreateScene("LessIsMorePhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("LessIsMoreBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 1);
                simulation.ApplyGlobalAttribute(995, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var def = c.Balance.Devices.Single(d => d.Id == 9024); CampaignStateUtility.CreateDevice(c, def, def.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>(); Check(collider != null && collider.convex && !collider.isTrigger, "less is more has solid cylindrical collision");
                for (int n = 0; n < 9; n++) MoreCompanion(c, true, n % 2 == 0);
                int companion = MoreCompanion(c, false);
                var spawn = board.transform.TransformPoint(new Vector3(0, .1f, .8f)); var incoming = board.transform.rotation * Vector3.back * 2;
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0; double expected = 1; int uses = 0;
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                foreach (bool placed in new[] { false, true, true, false })
                {
                    MorePlace(c, companion, placed); Check(bridge.TryMove(key, spawn, Vector3.zero), "leave previous physical contact"); Step();
                    int before = hits.Count; Check(bridge.TrySetVelocity(key, incoming), "approach solid surface"); Vector3 solved = default;
                    for (int n = 0; hits.Count == before && n < 100; n++)
                    { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); solved = body.velocity; simulation.AfterPhysics(bridge); }
                    Check(hits.Count == before + 1, "real collision enters once");
                    if (!placed) { expected += 2; uses++; }
                    Near(expected, c.Manager.GetComponentData<MarbleScore>(c.Marbles[key]).ScoreMultiplier, "physical impact uses current placed count");
                    Near(10, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "physical ball retains value");
                    Check(Vector3.Dot(body.velocity, incoming) < 0, "ball bounces normally"); VectorNear(solved, body.velocity, "Lua preserves solver velocity");
                    for (int n = 0; n < 5; n++) Step(); Check(hits.Count == before + 1, "continuous contact does not reapply bonus");
                    Near(20 - uses, simulation.GetOwnedDevices().Single(x => x.InstanceId == owned.InstanceId).RemainingDurability, "physical effective-use durability");
                }
                simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true }); Step();
                Near(50, c.Round.Score, "two plus-two effects settle as value times five");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
