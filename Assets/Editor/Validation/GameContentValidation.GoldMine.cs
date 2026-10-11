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
        private static void GoldMineLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                var device = AddGoldMine(f); var key = f.Launch();
                double coins = f.C.Campaign.Coins, blood = f.C.Player.Blood;
                var motion = f.Physics.States[key].LinearVelocity;
                f.Simulation.ApplyGlobalAttribute(976, GameAttribute.COIN_GAIN_RATE, AttributeOperation.Set, 2);
                for (int i = 1; i <= 6; i++)
                {
                    var hit = GoldMineHit(f, key, device); f.Step(hit, hit);
                    int effective = Math.Min(i, 5);
                    Near(coins + effective * 4, f.C.Campaign.Coins, "mine pays exactly four once per entry, up to twenty");
                    Near(10 - effective * 2, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "mine subtracts base value only for a payout");
                    Near(20 - effective, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "mine spends one durability only for a payout");
                    Check(f.C.Marbles.Count == 1, "mine does not consume the input ball");
                }
                Near(blood, f.C.Player.Blood, "mining neither charges nor refunds blood");
                Near(0, f.C.Round.Score, "mining directly awards coins, not round score");
                VectorNear(motion, f.Physics.States[key].LinearVelocity, "mining does not rewrite ball motion");
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("20/20") && f.Simulation.GetDeviceStatus(device.InstanceId).Contains("已采完"), "depleted mine displays this round's output");
                var other = AddGoldMine(f); f.Step(GoldMineHit(f, key, other));
                Near(coins + 24, f.C.Campaign.Coins, "another mine has its own cap and accepts a zero-value ball");
                Near(0, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "zero value cannot become negative");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == other.InstanceId).RemainingDurability, "second mine has independent wear");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Check(f.Simulation.BeginRound(), "mines survive into another round");
                var fresh = f.Launch(); double nextCoins = f.C.Campaign.Coins;
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("0/20"), "new round clears the depleted status even before collision");
                f.Step(GoldMineHit(f, fresh, device));
                Near(nextCoins + 4, f.C.Campaign.Coins, "new round replenishes mining output");
                Near(8, f.Simulation.GetMarbleAttribute(fresh, GameAttribute.BALL_BASE_VALUE), "new round applies normal value loss");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == device.InstanceId).RemainingDurability, "new round restores durability");
            }
            using (var f = new Fixture())
            {
                var device = AddGoldMine(f); double coins = f.C.Campaign.Coins;
                foreach (double value in new[] { 0, 1, 1.5, 2, 3 })
                {
                    var key = f.Launch(); AttributeRuntime.SetBase(f.C, f.Ball(key), GameAttribute.BALL_BASE_VALUE, value);
                    f.Step(GoldMineHit(f, key, device)); coins += 4;
                    Near(coins, f.C.Campaign.Coins, "zero or insufficient value still receives the full payout");
                    Near(Math.Max(0, value - 2), f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "value loss clamps at zero, including fractions");
                }
                Near(15, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "low-value triggers spend normal durability");
            }
            using (var f = new Fixture())
            {
                var device = AddGoldMine(f); var keys = Enumerable.Range(0, 8).Select(_ => f.Launch()).ToArray();
                double coins = f.C.Campaign.Coins;
                var ignored = GoldMineHit(f, keys[0], device); ignored.LinkKey = 123;
                f.Step(f.Hit(keys[0], device), ignored);
                Near(coins, f.C.Campaign.Coins, "trigger and link contacts are ignored");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "ignored contacts spend no durability");
                f.Step(keys.Select(key => GoldMineHit(f, key, device)).ToArray());
                Near(coins + 20, f.C.Campaign.Coins, "simultaneous balls share the limit without a global cooldown");
                for (int i = 0; i < keys.Length; i++)
                    Near(i < 5 ? 8 : 10, f.Simulation.GetMarbleAttribute(keys[i], GameAttribute.BALL_BASE_VALUE), "only the first five deterministic contacts lose value");
                Near(15, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "overflow contacts do not spend durability");
                f.Step(new MarbleContact { Key = keys[0], Kind = MarbleContactKind.Score, TargetId = 10, Multiplier = 1, RushDisabled = true });
                Near(8, f.C.Round.Score, "normal settlement uses the reduced value");
            }
            foreach (int durability in new[] { 1, -1 })
            {
                using (var f = new Fixture())
                {
                    f.C.Balance.Devices.Single(d => d.Id == 9010).Durability = durability;
                    var device = AddGoldMine(f); var key = f.Launch(); double coins = f.C.Campaign.Coins;
                    for (int i = 0; i < 7; i++) f.Step(GoldMineHit(f, key, device));
                    int uses = durability == 1 ? 1 : 5;
                    Near(coins + uses * 4, f.C.Campaign.Coins, "final durability pays in full; unlimited durability still obeys the round limit");
                    Near(10 - uses * 2, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "disabled effects never reduce value");
                    Near(durability == 1 ? 0 : -1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "finite and infinite durability retain their contracts");
                }
            }
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 9010); definition.Range = 10;
                var device = AddGoldMine(f); var key = f.Launch(); double coins = f.C.Campaign.Coins;
                for (int i = 0; i < 4; i++) f.Step(GoldMineHit(f, key, device));
                Near(coins + 10, f.C.Campaign.Coins, "custom non-divisible cap gives only the remaining coins on the last trigger");
                Near(4, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "partial last payout still counts as one trigger");
                Near(17, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "no charge after a custom cap");
            }
            using (var f = new Fixture())
            {
                var device = AddGoldMine(f); var key = f.Launch();
                f.Simulation.ApplyAttribute(key, 977, GameAttribute.BALL_BASE_VALUE, AttributeOperation.Multiply, 2);
                f.Step(GoldMineHit(f, key, device));
                Near(16, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "negative base delta preserves the existing multiplier exactly once");
                f.C.Manager.GetBuffer<AttributeModifierData>(f.Ball(key)).Clear(); AttributeRuntime.Recompute(f.C, f.Ball(key));
                Near(8, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "removing a modifier exposes only the actual base loss");
            }
            GoldMinePhysics();
            Debug.Log("GOLD_MINE_VALIDATION_PASSED: " + (assertions - start) + " assertions; payouts, zero-value inputs, round/instance limits, simultaneous hits, durability and real PhysX collision entries.");
        }

        private static OwnedDeviceData AddGoldMine(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9010));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static MarbleContact GoldMineHit(Fixture f, MarbleKey key, OwnedDeviceData device)
        {
            var hit = f.Hit(key, device); hit.HasImpact = true; hit.Normal = Vector3.back; hit.IncomingVelocity = Vector3.forward * 2;
            return hit;
        }

        private static void GoldMinePhysics()
        {
            var scene = SceneManager.CreateScene("GoldMinePhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, contact => { if (contact.HasImpact) hits.Add(contact); simulation.EnqueueContact(contact); });
            var board = new GameObject("GoldMineBoard"); SceneManager.MoveGameObjectToScene(board, scene);
            MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(978, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(979, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 0);
                var definition = c.Balance.Devices.Single(d => d.Id == 9010);
                CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                var owned = simulation.GetOwnedDevices().Single();
                CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, definition.Id));
                view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>();
                Check(collider != null && collider.convex && !collider.isTrigger, "mine package supplies a solid cylinder");
                var spawn = board.transform.TransformPoint(new Vector3(0, .1f, .8f));
                simulation.FireOnce(); simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                var key = c.Marbles.Keys.Single(); double coins = c.Campaign.Coins;
                for (int pass = 0; pass < 6; pass++)
                {
                    Check(bridge.TryMove(key, spawn, board.transform.rotation * Vector3.back * 2), "set up an independent physical mine impact");
                    for (int steps = 0; hits.Count <= pass && steps < 50; steps++)
                    { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                    Check(hits.Count == pass + 1, "physical collision entry registers once");
                    int effective = Math.Min(pass + 1, 5);
                    Near(coins + effective * 4, c.Campaign.Coins, "real collision invokes the packaged coin payout and cap");
                    Near(10 - effective * 2, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "real collision subtracts value without destroying the ball");
                    for (int steps = 0; steps < 10; steps++)
                    { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                    Check(hits.Count == pass + 1, "resting on the mine does not repeatedly trigger");
                    Near(coins + effective * 4, c.Campaign.Coins, "resting contact cannot farm extra coins");
                }
                Near(15, simulation.GetOwnedDevices().Single().RemainingDurability, "real capped mine only spends five uses");
                Check(collider.enabled, "hitting the output cap keeps normal solid collision for the rest of the round");
            }
            finally
            { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
