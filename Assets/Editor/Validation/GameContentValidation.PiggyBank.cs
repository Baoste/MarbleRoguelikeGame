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
        private static void PiggyBankLuaApi()
        {
            int start = assertions;
            foreach (long coins in new long[] { 0, 9, 10, 19, 20, 25, 100, 1000 })
                using (var f = new Fixture())
                {
                    var device = AddPiggyBank(f); var key = f.Launch(); PiggyCoins(f, coins);
                    var motion = f.Physics.States[key].LinearVelocity;
                    f.Step(PiggyHit(f, key, device));
                    double factor = 1 + .1 * (coins / 10);
                    Near(factor, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "pig uses whole ten-coin tiers");
                    Near(coins, f.C.Campaign.Coins, "pig never spends or awards coins");
                    Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "pig multiplies score without changing base value");
                    Near(coins < 10 ? 20 : 19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "unit multiplier costs no durability");
                    Near(0, f.C.Round.Score, "multiplier is applied at later settlement");
                    VectorNear(motion, f.Physics.States[key].LinearVelocity, "pig does not rewrite physical motion");
                    f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 10, Multiplier = 1, RushDisabled = true });
                    Near(10 * factor, f.C.Round.Score + f.C.Round.FractionalScore, "normal settlement includes the exact pig factor");
                }
            using (var f = new Fixture())
            {
                var device = AddPiggyBank(f); var key = f.Launch(); PiggyCoins(f, 25);
                var hit = PiggyHit(f, key, device); f.Step(hit, hit); f.Step(hit);
                Near(1.4, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "reentry adds factor minus one, duplicate same-step events do not");
                PiggyCoins(f, 15); f.Step(hit);
                Near(1.5, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "next impact adds the current bonus to earlier contributions");
                PiggyCoins(f, 9); f.Step(hit);
                Near(1.5, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "low balance never removes earlier gains");
                Near(17, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "only three effective impacts spend durability");
                PiggyCoins(f, 30); f.Step();
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("1.3"), "status refreshes when coins change without a collision");
                var other = AddPiggyBank(f); var second = f.Launch();
                f.Step(PiggyHit(f, key, other), PiggyHit(f, second, other));
                Near(1.8, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "independent pig adds to the shared device bonus");
                Near(1.3, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(second)).ScoreMultiplier, "different simultaneous balls do not share a cooldown");
                Near(18, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == other.InstanceId).RemainingDurability, "each simultaneous ball spends one use");
                var link = PiggyHit(f, key, other); link.LinkKey = 88;
                f.Step(f.Hit(key, other), link);
                Near(18, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == other.InstanceId).RemainingDurability, "trigger and link contacts do not count as impacts");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Check(f.Simulation.BeginRound(), "pig survives round reset"); var fresh = f.Launch(); PiggyCoins(f, 10);
                f.Step(PiggyHit(f, fresh, device));
                Near(1.1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(fresh)).ScoreMultiplier, "fresh ball has no previous round multiplier");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == device.InstanceId).RemainingDurability, "new round restores wear");
            }
            foreach (int durability in new[] { 1, 20, -1 })
                using (var f = new Fixture())
                {
                    f.C.Balance.Devices.Single(d => d.Id == 9012).Durability = durability;
                    var device = AddPiggyBank(f); var key = f.Launch(); PiggyCoins(f, 10);
                    for (int i = 0; i < 22; i++) f.Step(PiggyHit(f, key, device));
                    int uses = durability == -1 ? 22 : durability;
                    Near(1 + .1 * uses, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "final and infinite uses add the right number of bonuses");
                    Near(durability == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "finite and infinite wear remain correct");
                }
            using (var f = new Fixture())
            {
                var device = AddPiggyBank(f); var key = f.Launch(); PiggyCoins(f, CampaignStateUtility.MaxCoins);
                f.Step(PiggyHit(f, key, device));
                Near(1000, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "maximum wallet saturates the multiplier without throwing");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 10, Multiplier = 1, RushDisabled = true });
                Near(10 * f.C.Content.Definition(GameAttribute.MACHINE_MULT).MaxValue, f.C.Round.Score, "normal settlement still respects the shared machine multiplier cap");
            }
            PiggyWalletApi(); AdditiveDeviceScores(); PiggyPhysics();
            Debug.Log("PIGGY_BANK_VALIDATION_PASSED: " + (assertions - start) + " assertions; wallet tiers, additive device scores, unchanged coins/value, durability, API bounds and real impacts.");
        }

        private static OwnedDeviceData AddPiggyBank(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9012));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last(); CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void PiggyCoins(Fixture f, long value) { var campaign = f.C.Campaign; campaign.Coins = value; f.C.Campaign = campaign; }
        private static MarbleContact PiggyHit(Fixture f, MarbleKey key, OwnedDeviceData device)
        { var hit = f.Hit(key, device); hit.HasImpact = true; hit.Normal = Vector3.back; hit.IncomingVelocity = Vector3.forward * 2; return hit; }
        private static void PiggyWalletApi()
        {
            using (var f = new Fixture())
            {
                var device = AddPiggyBank(f); var key = f.Launch(); CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var entity);
                var api = new DeviceContactContext(f.C, f.Ball(key), entity, f.Hit(key, device), f.Physics.Poses[device.InstanceId]);
                var tick = new DeviceEventContext(f.C, entity);
                PiggyCoins(f, CampaignStateUtility.MaxCoins - 1);
                Near(CampaignStateUtility.MaxCoins - 1, api.Coins, "contact wallet read is exact at the largest odd balance");
                Near(api.Coins, tick.Coins, "event wallet read agrees without requiring a ball");
                PiggyCoins(f, 17); Near(17, api.Coins, "wallet getter reads the current state rather than a cached value");
                api.AddScoreMultiplier(1.2); api.MultiplyScore(1.2);
                Near(1.4, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "new API and legacy alias both accumulate factor minus one");
                ExpectArgument(() => api.AddScoreMultiplier(-1)); ExpectArgument(() => api.AddScoreMultiplier(double.NaN)); ExpectArgument(() => api.MultiplyScore(double.PositiveInfinity));
                Near(1.4, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "invalid contribution never mutates score");
                api.AddScoreMultiplier(1);
                Near(1.4, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "neutral factor contributes zero");
                api.AddScoreMultiplier(0);
                Near(.4, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "factor zero contributes minus one");
                api.AddScoreMultiplier(double.MaxValue);
                Near(1000, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "large finite contribution saturates safely");
                api.Close(); tick.Close();
                ExpectExpired(() => { _ = api.Coins; }); ExpectExpired(() => { _ = tick.Coins; }); ExpectExpired(() => api.AddScoreMultiplier(2)); ExpectExpired(() => api.MultiplyScore(2));
            }
        }

        private static void AdditiveDeviceScores()
        {
            using (var f = new Fixture())
            {
                var pig = AddPiggyBank(f); var multi = f.Add(f.C.Balance.Devices.Single(d => d.Id == 113));
                var key = f.Launch(); PiggyCoins(f, 25);
                f.Step(PiggyHit(f, key, pig)); f.Step(f.Hit(key, multi)); f.Step(PiggyHit(f, key, pig));
                Near(2.25, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "pig plus Multi Ding plus pig gives 1 + .2 + .85 + .2");
                Near(0, f.C.Round.Score, "all device contributions wait for settlement");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 10, Multiplier = 1, RushDisabled = true });
                Near(22.5, f.C.Round.Score + f.C.Round.FractionalScore, "score area multiplies value by the completed bonus once");
            }
            using (var f = new Fixture())
            {
                var key = f.Launch();
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Multiplier, TargetId = 7001, Multiplier = 2 },
                    new MarbleContact { Key = key, Kind = MarbleContactKind.RushPin, TargetId = 7002, Multiplier = 1, RushChanceAdd = .1 },
                    new MarbleContact { Key = key, Kind = MarbleContactKind.Multiplier, TargetId = 7003, Multiplier = 3 },
                    new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 10, Multiplier = 1, RushDisabled = true });
                Near(40, f.C.Round.Score, "legacy pins give 1 + (2 - 1) + (3 - 1) before same-step settlement; Rush pin adds nothing");
            }
            using (var f = new Fixture())
            {
                var bounce = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9000)); var pig = AddPiggyBank(f); var key = f.Launch(); PiggyCoins(f, 25);
                f.Simulation.ApplyDeviceAttribute(bounce.InstanceId, 991, GameAttribute.MACHINE_MULT, AttributeOperation.Set, 2);
                f.Step(f.Hit(key, bounce)); f.Step(PiggyHit(f, key, pig));
                Near(2.2, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "generic Trigger factor and Lua factor share the same bonus total");
                Near(1, f.Simulation.GetMarbleAttribute(key, GameAttribute.MACHINE_MULT), "generic trigger does not bake device factors into the base attribute");
            }
            using (var f = new Fixture())
            {
                var pig = AddPiggyBank(f); var key = f.Launch(); PiggyCoins(f, 25);
                f.Simulation.ApplyMarbleEffect(key, MarbleModifierAttribute.ScoreMul, 2); f.Step(PiggyHit(f, key, pig));
                Near(2.4, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "permanent non-device modifier scales the contribution total");
                f.Simulation.ApplyMarbleEffect(key, MarbleModifierAttribute.ScoreMul, 2); f.Step(PiggyHit(f, key, pig));
                Near(5.6, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "later device bonus retains independent permanent modifiers");
            }
            using (var f = new Fixture())
            {
                var pig = AddPiggyBank(f); var multi = f.Add(f.C.Balance.Devices.Single(d => d.Id == 113));
                var key = f.Launch(); PiggyCoins(f, 25); f.Step(PiggyHit(f, key, pig)); f.Step(f.Hit(key, multi));
                Check(ContentSpawnSystem.Children(f.C, f.Ball(key), 1, false, Vector3.forward, Vector3.up), "create a child after multiple device contributions");
                var child = f.C.Marbles.Keys.Single(k => !k.Equals(key)); f.Step(PiggyHit(f, child, pig));
                Near(2.25, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(child)).ScoreMultiplier, "child inherits the bonus total and can add another bonus");
                Near(2.05, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "child contribution does not change its parent's total");
            }
            var standalone = new MarbleScore { BaseMultiplier = 2, ScoreMultiplier = 2 };
            standalone.AddDeviceMultiplier(2); standalone.AddDeviceMultiplier(3);
            Near(8, standalone.ScoreMultiplier, "launcher multiplier stays separate from additive device bonuses");
            var reduced = new MarbleScore { BaseMultiplier = 1, ScoreMultiplier = 1 };
            reduced.AddDeviceMultiplier(0); reduced.AddDeviceMultiplier(0);
            Near(0, reduced.ScoreMultiplier, "negative combined bonus never produces negative score");
            reduced.AddDeviceMultiplier(3);
            Near(1, reduced.ScoreMultiplier, "negative bonus remains in the sum for later additions");
            using (var f = new Fixture())
            {
                var pig = AddPiggyBank(f); var key = f.Launch(); PiggyCoins(f, 50);
                f.Step(PiggyHit(f, key, pig)); f.Step(PiggyHit(f, key, pig));
                Near(2, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "two factors of 1.5 give 1 + 2 * (1.5 - 1)");
                Near(0, f.C.Round.Score, "the two hits only accumulate a bonus");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 10, Multiplier = 1, RushDisabled = true });
                Near(20, f.C.Round.Score + f.C.Round.FractionalScore, "score area applies the requested two-hit multiplier");
            }
        }

        private static void PiggyPhysics()
        {
            var scene = SceneManager.CreateScene("PiggyBankPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, contact => { if (contact.HasImpact) hits.Add(contact); simulation.EnqueueContact(contact); });
            var board = new GameObject("PiggyBankBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(982, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(983, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 0);
                var definition = c.Balance.Devices.Single(d => d.Id == 9012); CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                var campaign = c.Campaign; campaign.Coins = 25; c.Campaign = campaign;
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, definition.Id));
                view.Root.SetActive(true); Physics.SyncTransforms();
                Check(view.Root.GetComponent<CapsuleCollider>() != null && !view.Root.GetComponent<CapsuleCollider>().isTrigger, "pig package supplies a solid collision body");
                Check(view.Root.transform.Find("Snout") != null && view.Root.transform.Find("CoinSlot") != null, "runtime package includes the pig snout and coin slot");
                var spawn = board.transform.TransformPoint(new Vector3(0, .15f, .8f));
                simulation.FireOnce(); simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                for (int pass = 0; pass < 2; pass++)
                {
                    Check(bridge.TryMove(key, spawn, board.transform.rotation * Vector3.back * 2), "set up a fresh pig collision");
                    for (int step = 0; hits.Count <= pass && step < 50; step++)
                    { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                    Check(hits.Count == pass + 1, "real pig collision registers once per entry");
                    Near(1 + .2 * (pass + 1), c.Manager.GetComponentData<MarbleScore>(c.Marbles[key]).ScoreMultiplier, "real pig impact accumulates factor minus one");
                    for (int step = 0; step < 10; step++)
                    { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                    Check(hits.Count == pass + 1, "resting on the pig does not repeatedly multiply score");
                }
                Near(25, c.Campaign.Coins, "real impacts leave the wallet unchanged");
                Near(18, simulation.GetOwnedDevices().Single().RemainingDurability, "real impacts spend one use each");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
