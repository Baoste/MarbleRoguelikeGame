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
        private static OwnedDeviceData AddBloodFactory(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9019));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var e);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(e);
        }
        private static void SetTestBlood(Fixture f, float amount)
        { var player = f.C.Player; player.Blood = amount; f.C.Player = player; }
        private static void BloodFactoryLuaApi()
        {
            int start = assertions;
            foreach (double value in new[] { 0, .1, 1, 1.999, 2, 3, 5, 10, 11, 19.9, 10000 })
            using (var f = new Fixture())
            {
                var device = AddBloodFactory(f); var key = f.Launch(); var ball = f.Ball(key);
                AttributeRuntime.SetBase(f.C, ball, GameAttribute.BALL_BASE_VALUE, value); SetTestBlood(f, 100);
                var api = CashApi(f, key, device); api.AddScoreMultiplier(1.5); api.AddScoreMultiplier(1.5); api.Close();
                double coins = f.C.Campaign.Coins; var motion = f.Physics.States[key];
                var hit = GoldMineHit(f, key, device); f.Step(hit, hit);
                double remaining = Math.Floor(value / 2);
                Near(remaining, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "factory floors half the current value once per collision");
                Near(Math.Min(1000, 100 + remaining * 5), f.C.Player.Blood, "heal uses remaining value, excluding score multipliers, capped at capacity");
                Near(f.C.Player.Blood, f.Simulation.Snapshot.Blood, "HUD snapshot exposes restored spendable blood");
                Near(value > 0 ? 19 : 20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "positive value conversion uses once; zero value is free");
                Near(coins, f.C.Campaign.Coins, "factory gives blood rather than coins"); Near(0, f.C.Round.Score, "factory does not settle score");
                Near(2, f.C.Manager.GetComponentData<MarbleScore>(ball).ScoreMultiplier, "value reduction keeps accumulated multiplier");
                Check(f.C.Marbles.ContainsKey(key), "factory retains the original ball");
                VectorNear(motion.LinearVelocity, f.Physics.States[key].LinearVelocity, "Lua leaves natural bounce alone");
                f.Step(new MarbleContact { Key = key, TargetId = 700, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true });
                Near(remaining * 2, f.C.Round.Score + f.C.Round.FractionalScore, "reduced value settles with the preserved multiplier");
            }
            using (var f = new Fixture())
            {
                var device = AddBloodFactory(f); var a = f.Launch(); var b = f.Launch(); SetTestBlood(f, 999);
                var ignored = GoldMineHit(f, a, device); ignored.LinkKey = 123;
                f.Step(f.Hit(a, device), ignored); Near(999, f.C.Player.Blood, "trigger/link events do not heal");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "ignored events are free");
                f.Step(GoldMineHit(f, a, device), GoldMineHit(f, b, device));
                Near(1000, f.C.Player.Blood, "multiple impacts cannot exceed capacity");
                Near(5, f.Simulation.GetMarbleAttribute(a, GameAttribute.BALL_BASE_VALUE), "first ball halves even with partial healing");
                Near(5, f.Simulation.GetMarbleAttribute(b, GameAttribute.BALL_BASE_VALUE), "full blood still processes positive value");
                Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "two simultaneous conversions spend two uses");
                SetTestBlood(f, 100); f.Step(GoldMineHit(f, a, device)); Near(110, f.C.Player.Blood, "same ball may trigger again on a later entry");
                Near(2, f.Simulation.GetMarbleAttribute(a, GameAttribute.BALL_BASE_VALUE), "reentry halves the newly reduced value");
                var other = AddBloodFactory(f); f.Step(GoldMineHit(f, a, other)); Near(115, f.C.Player.Blood, "other instance applies independently");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == other.InstanceId).RemainingDurability, "other instance has independent wear");
            }
            foreach (int durability in new[] { 1, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9019).Durability = durability;
                var device = AddBloodFactory(f); var key = f.Launch(); SetTestBlood(f, 100);
                for (int i = 0; i < 8; i++) f.Step(GoldMineHit(f, key, device));
                Near(durability == 1 ? 125 : 140, f.C.Player.Blood, "last use heals in full; infinite wear still respects value exhaustion");
                Near(durability == 1 ? 5 : 0, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "no value loss after finite wear ends");
                Near(durability == 1 ? 0 : -1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "finite/infinite wear semantics");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Check(f.Simulation.BeginRound(), "factory remains available next round"); var fresh = f.Launch(); SetTestBlood(f, 100);
                f.Step(GoldMineHit(f, fresh, device)); Near(125, f.C.Player.Blood, "new round restores usable factory");
            }
            foreach (AttributeOperation operation in new[] { AttributeOperation.Add, AttributeOperation.Multiply, AttributeOperation.Set })
            using (var f = new Fixture())
            {
                var device = AddBloodFactory(f); var key = f.Launch(); SetTestBlood(f, 100);
                f.Simulation.ApplyAttribute(key, 976, GameAttribute.BALL_BASE_VALUE, operation, operation == AttributeOperation.Multiply ? 1.5 : 3, 5);
                f.Simulation.ApplyAttribute(key, 977, GameAttribute.BALL_MASS, AttributeOperation.Multiply, 2, 5);
                double expected = Math.Floor(f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE) / 2);
                f.Step(GoldMineHit(f, key, device)); Near(expected, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "current value replacement does not double-apply existing modifiers");
                Near(100 + expected * 5, f.C.Player.Blood, "heal agrees with actual value after conversion");
                Near(2, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_MASS), "other attribute modifiers are retained");
                for (int i = 0; i < 6; i++) f.Step();
                Near(expected, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "old value modifier expiry cannot undo conversion");
                Near(1, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_MASS), "unrelated temporary effects still expire normally");
            }
            using (var f = new Fixture())
            {
                var device = AddBloodFactory(f); var key = f.Launch(); SetTestBlood(f, 0); f.Step();
                Check(f.Simulation.Snapshot.Phase == RoundPhase.Draining, "out of blood waits for remaining balls");
                f.Step(GoldMineHit(f, key, device)); Near(25, f.C.Player.Blood, "remaining ball can restore blood after exhaustion");
                Check(f.Simulation.Snapshot.Phase == RoundPhase.Playing, "restored affordable blood reopens firing");
                Near(0, f.C.Launcher.FireRequested, "healing does not request a shot");
                f.Launch(); Near(24, f.C.Player.Blood, "restored blood pays for another real launch");
                Check(f.C.Marbles.Count == 2, "new launch actually creates a second ball");
            }
            BloodApiValidation();
            foreach (float dt in new[] { .01f, .02f }) BloodFactoryPhysics(dt);
            Debug.Log("BLOOD_FACTORY_VALIDATION_PASSED: " + (assertions - start) + " assertions; floor/value modifiers, capped healing, launch recovery, cashout, wear, API scope and real collisions.");
        }
        private static void BloodApiValidation()
        {
            using (var f = new Fixture())
            {
                var device = AddBloodFactory(f); var key = f.Launch(); SetTestBlood(f, 0); f.Step(); var api = CashApi(f, key, device);
                Near(0, api.Blood, "blood API reads player resource"); Near(1000, api.BloodCapacity, "blood API reads configured capacity");
                Near(.5, api.RestoreBlood(.5), "blood API supports fractional healing");
                Check(f.Simulation.Snapshot.Phase == RoundPhase.Draining, "insufficient restoration does not reopen firing");
                Near(.5, api.RestoreBlood(.5), "subsequent heal fills affordability threshold");
                Check(f.Simulation.Snapshot.Phase == RoundPhase.Playing, "minimum affordable shot resumes play");
                Near(0, api.RestoreBlood(0), "zero heal is a no-op"); Near(999, api.RestoreBlood(double.MaxValue), "huge finite healing safely saturates");
                Near(0, api.RestoreBlood(5), "full blood returns zero actual healing");
                ExpectArgument(() => api.RestoreBlood(-1)); ExpectArgument(() => api.RestoreBlood(double.NaN)); ExpectArgument(() => api.RestoreBlood(double.PositiveInfinity));
                ExpectArgument(() => api.SetCurrent("BALL_BASE_VALUE", double.NaN));
                Near(10, api.Value, "invalid exact value assignment does not change ball");
                Near(20, api.RemainingDurability, "blood and attribute primitives do not spend durability");
                api.Close(); ExpectExpired(() => api.RestoreBlood(1)); ExpectExpired(() => { _ = api.Blood; });
                ExpectExpired(() => { _ = api.BloodCapacity; }); ExpectExpired(() => api.SetCurrent("BALL_BASE_VALUE", 1));
                CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var entity);
                var eventApi = new DeviceEventContext(f.C, entity); SetTestBlood(f, 10);
                Near(10, eventApi.Blood, "event context reads blood without a ball"); Near(1000, eventApi.BloodCapacity, "event context shares capacity");
                Near(2.5, eventApi.RestoreBlood(2.5), "event context shares healing primitive");
                eventApi.Close(); ExpectExpired(() => eventApi.RestoreBlood(1)); ExpectExpired(() => { _ = eventApi.Blood; });
                var round = f.C.Round; round.CashoutRequested = true; round.Phase = (byte)RoundPhase.Draining; f.C.Round = round;
                api = CashApi(f, key, device); Near(5, api.RestoreBlood(5), "healing still updates resource during explicit cashout");
                Check(f.Simulation.Snapshot.Phase == RoundPhase.Draining, "healing cannot cancel an explicit cashout");
                round = f.C.Round; round.SettlementComplete = true; f.C.Round = round;
                Near(0, api.RestoreBlood(5), "completed settlement cannot receive healing"); api.Close();
            }
        }
        private static void BloodFactoryPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("BloodFactoryPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("BloodFactoryBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(978, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(979, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 0);
                var def = c.Balance.Devices.Single(d => d.Id == 9019); CampaignStateUtility.CreateDevice(c, def, def.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>(); Check(collider != null && collider.convex && !collider.isTrigger, "factory has solid cylinder collision");
                var spawn = board.transform.TransformPoint(new Vector3(0, .1f, .8f));
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                var player = c.Player; player.Blood = 100; c.Player = player;
                AttributeRuntime.SetBase(c, c.Marbles[key], GameAttribute.BALL_BASE_VALUE, 11);
                double expectedValue = 11, expectedBlood = 100; int uses = 0;
                for (int pass = 0; pass < 5; pass++)
                {
                    Check(bridge.TryMove(key, spawn, board.transform.rotation * Vector3.back * 2), "prepare physical reentry");
                    for (int n = 0; hits.Count <= pass && n < 100; n++)
                    { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                    Check(hits.Count == pass + 1, "physical collision enters once");
                    if (expectedValue > 0) { expectedValue = Math.Floor(expectedValue / 2); expectedBlood += expectedValue * 5; uses++; }
                    Near(expectedValue, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "physical hit executes packaged floor conversion");
                    Near(expectedBlood, simulation.Snapshot.Blood, "physical hit restores actual player blood");
                    for (int n = 0; n < 8; n++) { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                    Near(expectedBlood, simulation.Snapshot.Blood, "resting contact cannot repeatedly heal");
                    Near(20 - uses, simulation.GetOwnedDevices().Single().RemainingDurability, "physical effective-hit durability");
                }
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
