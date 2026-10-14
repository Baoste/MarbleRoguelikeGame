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
        private static void IcedAmericanoLuaApi()
        {
            int start = assertions;
            foreach (double value in new[] { 0, .1, 1, 3, 5.5, 10, 10000 })
            using (var f = new Fixture())
            {
                var device = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9032)); var key = f.Launch(); var ball = f.Ball(key);
                AttributeRuntime.SetBase(f.C, ball, GameAttribute.BALL_BASE_VALUE, value);
                var score = f.C.Manager.GetComponentData<MarbleScore>(ball); score.SetDeviceMultiplier(3); f.C.Manager.SetComponentData(ball, score);
                var motion = f.Physics.States[key]; double blood = f.C.Player.Blood, coins = f.C.Campaign.Coins;
                var hit = GoldMineHit(f, key, device); f.Step(hit, hit);
                Near(value / 2, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "coffee halves current base value without rounding");
                Near(27, f.C.Manager.GetComponentData<MarbleRush>(ball).Contribution, "extra twenty plus generic five counted once, including zero-value balls");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "one valid impact spends one wear");
                Near(0, f.C.Round.RushMeter, "coffee records contribution without immediate deposit");
                Near(3, f.C.Manager.GetComponentData<MarbleScore>(ball).ScoreMultiplier, "coffee preserves accumulated multiplier");
                Near(blood, f.C.Player.Blood, "coffee does not change blood"); Near(coins, f.C.Campaign.Coins, "coffee does not grant coins");
                VectorNear(motion.LinearVelocity, f.Physics.States[key].LinearVelocity, "coffee leaves physical bounce unchanged");
                f.Step(hit);
                Near(value / 4, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "later valid collision halves again");
                Near(52, f.C.Manager.GetComponentData<MarbleRush>(ball).Contribution, "later impact adds twenty five again");
                Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "later collision spends another wear");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 806, Multiplier = 1 });
                Near(78, f.C.Round.RushMeter, "score zone deposits contribution times one point five");
                Near(value / 4 * 3, f.C.Round.Score + f.C.Round.FractionalScore, "score zone uses reduced value and original multiplier");
            }
            using (var f = new Fixture())
            {
                var device = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9032)); var key = f.Launch();
                var link = GoldMineHit(f, key, device); link.LinkKey = 123;
                f.Step(link, f.Hit(key, device));
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "link and non-impact callbacks do not change value");
                Near(2, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).Contribution, "ignored contacts add no contribution");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "ignored contacts are free");
                var second = f.Launch(); f.Step(GoldMineHit(f, key, device), GoldMineHit(f, second, device));
                Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "simultaneous balls each spend one use");
                var cash = AddCashOut(f); var api = CashApi(f, key, cash);
                Check(api.HasTriggeredOtherDevice, "coffee records effective device history for later effects"); api.Close();
                var drain = new MarbleContact { Key = key, Kind = MarbleContactKind.Drain, TargetId = 807 }; f.Step(drain, drain);
                Near(27, f.C.Round.RushMeter, "drain deposits unscaled contribution once");
            }
            foreach (int durability in new[] { 1, 20, -1 })
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 9032); definition.Durability = durability;
                var device = f.Add(definition); var key = f.Launch();
                for (int i = 1; i <= 22; i++)
                {
                    f.Step(GoldMineHit(f, key, device)); int uses = durability < 0 ? i : Math.Min(i, durability);
                    Near(10 / Math.Pow(2, uses), f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "finite final use completes; exhausted device no longer halves");
                    Near(2 + 25 * uses, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).Contribution, "contribution stops only when durability runs out");
                    Near(durability < 0 ? -1 : durability - uses, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "finite and infinite wear");
                }
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Check(f.Simulation.BeginRound(), "coffee starts a new round"); var fresh = f.Launch(); f.Step(GoldMineHit(f, fresh, device));
                Near(5, f.Simulation.GetMarbleAttribute(fresh, GameAttribute.BALL_BASE_VALUE), "new round restores coffee durability");
                Near(27, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(fresh)).Contribution, "new ball has independent contribution");
            }
            foreach (var operation in new[] { AttributeOperation.Add, AttributeOperation.Multiply, AttributeOperation.Set })
            using (var f = new Fixture())
            {
                var device = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9032)); var key = f.Launch();
                f.Simulation.ApplyAttribute(key, 996, GameAttribute.BALL_BASE_VALUE, operation, operation == AttributeOperation.Multiply ? 1.5 : 3, 5);
                f.Simulation.ApplyAttribute(key, 997, GameAttribute.BALL_MASS, AttributeOperation.Multiply, 2, 5);
                double expected = f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE) / 2;
                f.Step(GoldMineHit(f, key, device));
                Near(expected, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "halving current value consumes old value modifiers exactly once");
                Near(2, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_MASS), "unrelated modifiers survive conversion");
                for (int i = 0; i < 6; i++) f.Step();
                Near(expected, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "modifier expiry cannot reverse conversion");
            }
            using (var f = new Fixture())
            {
                var device = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9032)); var key = f.Launch();
                var round = f.C.Round; round.RushRemaining = 1; f.C.Round = round;
                f.Step(GoldMineHit(f, key, device));
                Near(5, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "active Rush does not get baked into halved base value");
                Near(10, RushSystem.BallValue(f.C, f.Ball(key)), "temporary Rush five remains independent");
                Near(27, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).Contribution, "device gain is not doubled during Rush");
                for (int i = 0; i < 12; i++) f.Step();
                Near(5, RushSystem.BallValue(f.C, f.Ball(key)), "temporary value disappears without reversing coffee effect");
                round = f.C.Round; round.RushMeter = 60; f.C.Round = round;
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 808, Multiplier = 1 });
                Near(10, f.C.Round.RushRemaining, "coffee-enhanced contribution can fill the shared bar");
                Near(0, f.C.Round.RushMeter, "activation consumes filled meter");
            }
            foreach (float dt in new[] { .01f, .02f }) IcedAmericanoPhysics(dt);
            Debug.Log("ICED_AMERICANO_VALIDATION_PASSED: " + (assertions - start) + " assertions; value, contribution, settlement, wear, modifiers and real packaged collisions.");
        }

        private static void IcedAmericanoPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("IcedAmericanoPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); int impacts = 0;
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) impacts++; simulation.EnqueueContact(hit); });
            var board = new GameObject("IcedAmericanoBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(998, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(999, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, .8);
                var definition = c.Balance.Devices.Single(d => d.Id == 9032); CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                int id = c.Campaign.NextDeviceId; MorePlace(c, id, true);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(simulation.GetOwnedDevices().Single(), board.transform, c.DeviceScripts.Get(id, definition.Id));
                view.Root.SetActive(true); Physics.SyncTransforms();
                var colliders = view.Root.GetComponentsInChildren<Collider>(true);
                Check(colliders.Length == 1 && !colliders[0].isTrigger, "coffee cup has one solid collider");
                Check(view.Root.transform.Find("Cup") != null && view.Root.transform.Find("Ice6") != null && view.Root.transform.Find("Straw") != null, "package includes cup, six ice cubes and straw");
                var spawn = board.transform.TransformPoint(new Vector3(0, .285f, .9f));
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0; var incoming = board.transform.rotation * Vector3.back * 3;
                for (int pass = 0; pass < 2; pass++)
                {
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "leave previous cup collision"); Step();
                    Check(bridge.TrySetVelocity(key, incoming), "approach coffee cup"); int before = impacts; Vector3 solved = default;
                    for (int i = 0; impacts == before && i < 200; i++)
                    {
                        simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); solved = body.velocity; simulation.AfterPhysics(bridge);
                    }
                    Near(before + 1, impacts, "one physical impact per approach");
                    Near(10 / Math.Pow(2, pass + 1), simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "physical impact runs packaged halving Lua");
                    Near(2 + 25 * (pass + 1), c.Manager.GetComponentData<MarbleRush>(c.Marbles[key]).Contribution, "physical impact adds complete contribution once");
                    Near(19 - pass, simulation.GetOwnedDevices().Single().RemainingDurability, "physical effective-hit wear");
                    VectorNear(solved, body.velocity, "Lua preserves natural collision response");
                    Check(Vector3.Dot(body.velocity, incoming) < 0, "ball bounces away from cup");
                }
                Near(0, c.Round.RushMeter, "physical cup does not deposit Rush before settlement");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
