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
        private static readonly int[] MalfunctionFactors = { 0, 1, 2, 3, 5, 7, 11, 13 };
        private static OwnedDeviceData AddMalfunction(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9022));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static uint MalfunctionSeed(int factor)
        {
            for (uint seed = 1; seed < 100000; seed++)
            {
                uint state = seed;
                if (MalfunctionFactors[(int)(MarbleRules.NextRandom(ref state) * 8)] == factor) return seed;
            }
            throw new Exception("Cannot find a seed for outcome " + factor);
        }
        private static void MalfunctionRandom(Fixture f, int factor)
        { var round = f.C.Round; round.RandomState = MalfunctionSeed(factor); f.C.Round = round; }
        private static void MalfunctionLuaApi()
        {
            int start = assertions;
            foreach (int factor in MalfunctionFactors)
            using (var f = new Fixture())
            {
                var d = AddMalfunction(f); var key = f.Launch(); var api = CashApi(f, key, d); api.AddScoreMultiplier(4); api.Close();
                var motion = f.Physics.States[key]; double blood = f.C.Player.Blood, coins = f.C.Campaign.Coins;
                MalfunctionRandom(f, factor); uint expectedRandom = f.C.Round.RandomState; MarbleRules.NextRandom(ref expectedRandom);
                var hit = PiggyHit(f, key, d); f.Step(hit, hit);
                double expected = factor == 0 ? 0 : 4 + factor - 1;
                Near(expected, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "sampled factor resets at zero or adds factor minus one");
                Near(expectedRandom, f.C.Round.RandomState, "one effective collision uses exactly one round RNG draw");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "all eight outcomes including zero and one cost one use");
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "random factor does not alter base value");
                Near(0, f.C.Round.Score, "collision never settles score early"); Near(blood, f.C.Player.Blood, "collision does not change blood"); Near(coins, f.C.Campaign.Coins, "collision does not change coins");
                VectorNear(motion.LinearVelocity, f.Physics.States[key].LinearVelocity, "normal solver bounce remains unchanged");
                Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains(factor == 0 ? "归零" : "×" + factor), "status displays the sampled result");
                var other = AddCashOut(f); api = CashApi(f, key, other); Check(api.HasTriggeredOtherDevice, "zero and neutral outcomes both record successful history"); api.Close();
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(10 * expected, f.C.Round.Score + f.C.Round.FractionalScore, "normal score area applies final accumulated factor once");
            }
            MalfunctionResets(); MalfunctionRandomAndWear(); DeviceMultiplierSetApi();
            foreach (float dt in new[] { .01f, .02f }) MalfunctionPhysics(dt);
            Debug.Log("MALFUNCTION_VALIDATION_PASSED: " + (assertions - start) + " assertions; eight seeded outcomes, reset then recovery, additive settlement, wear, inheritance and real collisions.");
        }
        private static void MalfunctionResets()
        {
            foreach (bool pending in new[] { false, true })
            using (var f = new Fixture())
            {
                var d = AddMalfunction(f); var key = f.Launch(); double expected = 1;
                foreach (int factor in new[] { 13, 11, 0, 1, 3, 2, 0, 0, 5 })
                {
                    MalfunctionRandom(f, factor); f.Step(PiggyHit(f, key, d)); expected = factor == 0 ? 0 : expected + factor - 1;
                    Near(expected, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "zero clears the full accumulated value; later factors can recover");
                }
                Near(11, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "each valid repeat is one use");
                f.Step(new MarbleContact { Key = key, Kind = pending ? MarbleContactKind.RandomScore : MarbleContactKind.Score,
                    TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(40, pending ? f.C.Round.PendingScore + f.C.Round.FractionalPendingScore : f.C.Round.Score + f.C.Round.FractionalScore, "recovered multiplier settles through ordinary or pending scoring");
            }
            using (var f = new Fixture())
            {
                var d = AddMalfunction(f); var pig = AddPiggyBank(f); var key = f.Launch(); PiggyCoins(f, 50);
                f.Step(PiggyHit(f, key, pig)); MalfunctionRandom(f, 0); f.Step(PiggyHit(f, key, d));
                Near(0, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "zero removes contributions from all previous devices");
                f.Step(PiggyHit(f, key, pig)); Near(.5, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "another device can add half a point after zero");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(5, f.C.Round.Score, "zero is not a persistent no-score flag");
            }
            using (var f = new Fixture())
            {
                var d = AddMalfunction(f); var splitter = AddSplitter(f); var key = f.Launch();
                MalfunctionRandom(f, 0); f.Step(PiggyHit(f, key, d)); f.Step(f.Hit(key, splitter));
                Near(3, f.C.Marbles.Count, "zero multiplier ball still splits normally");
                foreach (var child in f.C.Marbles.Keys.ToArray())
                {
                    var api = CashApi(f, child, d); Near(0, api.DeviceScoreMultiplier, "split children inherit zero accumulated factor");
                    api.AddScoreMultiplier(3); Near(2, api.DeviceScoreMultiplier, "split children can recover independently"); api.Close();
                }
                var copiedFrom = f.C.Marbles.Keys.First(); var copyApi = CashApi(f, copiedFrom, d); copyApi.SetDeviceScoreMultiplier(0);
                var copy = copyApi.CreateCopy(); Check(copy.TrySpawn(Vector3.one * 3, Vector3.zero), "zero factor can be copied"); copyApi.Close();
                var copied = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); copyApi = CashApi(f, copied, d);
                Near(0, copyApi.DeviceScoreMultiplier, "copy inherits zero"); copyApi.AddScoreMultiplier(2); Near(1, copyApi.DeviceScoreMultiplier, "copy also recovers without touching parent"); copyApi.Close();
                copyApi = CashApi(f, copiedFrom, d); Near(0, copyApi.DeviceScoreMultiplier, "child recovery does not change parent"); copyApi.Close();
            }
        }
        private static void MalfunctionRandomAndWear()
        {
            foreach (int wear in new[] { 1, 20, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9022).Durability = wear;
                var d = AddMalfunction(f); var key = f.Launch();
                for (int i = 0; i < 22; i++) { MalfunctionRandom(f, 2); f.Step(PiggyHit(f, key, d)); }
                int uses = wear == -1 ? 22 : wear;
                Near(1 + uses, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "last durability applies and unlimited continues");
                Near(wear == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "finite/unlimited wear");
                if (wear != -1) { MalfunctionRandom(f, 13); uint seed = f.C.Round.RandomState; f.Step(PiggyHit(f, key, d)); Near(seed, f.C.Round.RandomState, "exhausted device does not advance RNG"); }
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "new round restores malfunction device");
                key = f.Launch(); Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("等待"), "new round clears previous outcome status");
                MalfunctionRandom(f, 3); f.Step(PiggyHit(f, key, d)); Near(3, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "new ball starts from base factor one");
            }
            using (var f = new Fixture())
            {
                var d = AddMalfunction(f); var a = f.Launch(); var b = f.Launch();
                MalfunctionRandom(f, 5); uint seed = f.C.Round.RandomState;
                var link = PiggyHit(f, a, d); link.LinkKey = 88; f.Step(f.Hit(a, d), link);
                Near(seed, f.C.Round.RandomState, "trigger-only and link contacts do not draw"); Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "ignored events cost no wear");
                uint next = seed; int first = MalfunctionFactors[(int)(MarbleRules.NextRandom(ref next) * 8)]; int second = MalfunctionFactors[(int)(MarbleRules.NextRandom(ref next) * 8)];
                f.Step(PiggyHit(f, a, d), PiggyHit(f, a, d), PiggyHit(f, b, d));
                Near(first, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "first simultaneous ball gets its own draw");
                Near(second, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(b)).ScoreMultiplier, "second simultaneous ball is not blocked by device cooldown");
                Near(next, f.C.Round.RandomState, "two distinct contacts advance RNG twice"); Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "two distinct balls spend two uses");
                var other = AddMalfunction(f); MalfunctionRandom(f, 7); f.Step(PiggyHit(f, a, other));
                Near(first + 6, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "another instance shares normal additive score");
                Near(19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == other.InstanceId).RemainingDurability, "other device has independent wear");
            }
            // Fixed, reproducible stream exercises every bucket without relying on nondeterministic sampling.
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9022).Durability = -1;
                var d = AddMalfunction(f); var key = f.Launch(); f.Simulation.ApplyAttribute(key, 996, GameAttribute.BALL_LIFETIME, AttributeOperation.Set, 120);
                var round = f.C.Round; round.RandomState = 0x12345678; f.C.Round = round;
                double expected = 1; var counts = new int[8]; uint state = round.RandomState;
                for (int n = 0; n < 256; n++)
                {
                    int index = (int)(MarbleRules.NextRandom(ref state) * 8); counts[index]++; int factor = MalfunctionFactors[index];
                    expected = factor == 0 ? 0 : Math.Min(1000, expected + factor - 1);
                    f.Step(PiggyHit(f, key, d));
                    Near(expected, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "packaged Lua follows round-seeded eight-way random stream");
                }
                Check(counts.All(x => x > 0), "fixed stream reaches all eight outcomes"); Near(state, f.C.Round.RandomState, "random stream is deterministic and one draw per hit");
            }
        }
        private static void DeviceMultiplierSetApi()
        {
            using (var f = new Fixture())
            {
                var d = AddMalfunction(f); var key = f.Launch(); f.Simulation.ApplyMarbleEffect(key, MarbleModifierAttribute.ScoreMul, 2);
                f.Simulation.ApplyMarbleEffect(key, MarbleModifierAttribute.ScoreAdd, 7);
                var api = CashApi(f, key, d);
                foreach (double amount in new[] { 0d, .5d, 1d, 3.25d, 1000d })
                {
                    api.SetDeviceScoreMultiplier(amount); Near(amount, api.DeviceScoreMultiplier, "setter accepts a full factor");
                    var score = f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)); Near(2, score.BaseMultiplier, "setter preserves non-device multiplier");
                    Near(Math.Min(1000, amount * 2), score.ScoreMultiplier, "display score updates immediately with independent base multiplier");
                    Near(7, score.ScoreBonus, "setter preserves flat bonus"); Near(10, api.Value, "setter preserves ball value");
                }
                ExpectArgument(() => api.SetDeviceScoreMultiplier(-1)); ExpectArgument(() => api.SetDeviceScoreMultiplier(double.NaN));
                ExpectArgument(() => api.SetDeviceScoreMultiplier(double.PositiveInfinity)); ExpectArgument(() => api.SetDeviceScoreMultiplier(1000.001));
                Near(1000, api.DeviceScoreMultiplier, "invalid setter does not mutate factor");
                api.SetDeviceScoreMultiplier(0); api.AddScoreMultiplier(3); Near(2, api.DeviceScoreMultiplier, "zero setter leaves an ordinary recoverable bonus of minus one");
                Near(20, api.RemainingDurability, "setter itself does not register durability");
                api.Close(); ExpectExpired(() => api.SetDeviceScoreMultiplier(0));
            }
        }
        private static void MalfunctionPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("MalfunctionPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("MalfunctionBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 1);
                simulation.ApplyGlobalAttribute(995, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var def = c.Balance.Devices.Single(d => d.Id == 9022); CampaignStateUtility.CreateDevice(c, def, def.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>(); Check(collider != null && collider.convex && !collider.isTrigger, "malfunction has solid cylindrical collision");
                var spawn = board.transform.TransformPoint(new Vector3(0, .1f, .8f)); var incoming = board.transform.rotation * Vector3.back * 2;
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0; double expected = 1; int pass = 0;
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                foreach (int factor in new[] { 13, 0, 1, 3 })
                {
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "leave old collision before reentry"); Step();
                    var round = c.Round; round.RandomState = MalfunctionSeed(factor); c.Round = round;
                    Check(bridge.TrySetVelocity(key, incoming), "approach malfunction surface"); Vector3 solved = default;
                    for (int n = 0; hits.Count <= pass && n < 100; n++)
                    { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); solved = body.velocity; simulation.AfterPhysics(bridge); }
                    pass++; Check(hits.Count == pass, "real collision triggers once");
                    expected = factor == 0 ? 0 : expected + factor - 1;
                    Near(expected, c.Manager.GetComponentData<MarbleScore>(c.Marbles[key]).ScoreMultiplier, "physical collision executes packaged random factor and recoverable reset");
                    Near(10, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "physical ball keeps its base value");
                    Check(Vector3.Dot(body.velocity, incoming) < 0, "original ball bounces normally"); VectorNear(solved, body.velocity, "Lua does not replace solver velocity");
                    for (int n = 0; n < 5; n++) Step(); Check(hits.Count == pass, "continuous contact does not draw repeatedly");
                    Near(20 - pass, simulation.GetOwnedDevices().Single().RemainingDurability, "physical impact spends exactly one use");
                }
                simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true }); Step();
                Near(20, c.Round.Score, "real ball scores normally after zero then three");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
