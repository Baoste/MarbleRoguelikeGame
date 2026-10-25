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
        private static OwnedDeviceData AddMoneyTree(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9014));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void MoneyTreeLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                var tree = AddMoneyTree(f); var key = f.Launch(); double coins = f.C.Campaign.Coins;
                var state = f.Physics.States[key]; double blood = f.C.Player.Blood;
                var hit = f.Hit(key, tree);
                for (int i = 1; i <= 21; i++)
                {
                    f.Step(hit, hit); // Duplicate reports within one step are a single entry.
                    Near(coins + 5 * (i / 10), f.C.Campaign.Coins, "one payout per ten entries");
                    Near(20 - i / 10, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "counting is free; only payout spends durability");
                    Check(f.Simulation.GetDeviceStatus(tree.InstanceId).Contains((i % 10) + "/10"), "progress remainder is visible");
                }
                Check(f.C.Marbles.Count == 1, "counted ball is retained");
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "tree leaves value unchanged");
                Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "tree does not add score factors");
                VectorNear(state.Position, f.Physics.States[key].Position, "Lua does not reposition the ball");
                VectorNear(state.LinearVelocity, f.Physics.States[key].LinearVelocity, "Lua does not alter speed or direction");
                Near(0, f.Physics.Moves, "tree issues no move commands"); Near(blood, f.C.Player.Blood, "tree neither costs nor refunds blood");
                Near(0, f.C.Round.Score, "coins are awarded directly, not through round score");
                var link = hit; link.LinkKey = 123; var impact = hit; impact.HasImpact = true; impact.Normal = Vector3.up;
                f.Step(link); f.Step(impact);
                Check(f.Simulation.GetDeviceStatus(tree.InstanceId).Contains("1/10"), "link and solid impact events do not count");
                var other = AddMoneyTree(f); f.Step(f.Hit(key, other));
                Check(f.Simulation.GetDeviceStatus(other.InstanceId).Contains("1/10"), "each tree counts independently");
                var query = CashApi(f, key, other); Check(query.HasTriggeredOtherDevice, "successful counting records Lua-only history"); query.Close();
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Check(f.Simulation.BeginRound(), "tree survives another round"); var fresh = f.Launch();
                Check(f.Simulation.GetDeviceStatus(tree.InstanceId).Contains("0/10"), "round reset discards partial progress");
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == tree.InstanceId).RemainingDurability, "round reset restores wear");
                coins = f.C.Campaign.Coins;
                for (int i = 0; i < 9; i++) f.Step(f.Hit(fresh, tree));
                Near(coins, f.C.Campaign.Coins, "nine fresh entries cannot use the previous round remainder");
                f.Step(f.Hit(fresh, tree)); Near(coins + 5, f.C.Campaign.Coins, "tenth fresh entry pays");
            }
            using (var f = new Fixture())
            {
                var tree = AddMoneyTree(f); var keys = Enumerable.Range(0, 21).Select(_ => f.Launch()).ToArray();
                double coins = f.C.Campaign.Coins;
                f.Step(keys.Select(k => f.Hit(k, tree)).ToArray());
                Near(coins + 10, f.C.Campaign.Coins, "simultaneous distinct balls can complete several batches");
                Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "two simultaneous batches spend two uses");
                Check(f.C.Marbles.Count == 21 && f.Simulation.GetDeviceStatus(tree.InstanceId).Contains("1/10"), "all balls pass with one remainder");
            }
            foreach (int wear in new[] { 1, 20, -1 })
                using (var f = new Fixture())
                {
                    f.C.Balance.Devices.Single(d => d.Id == 9014).Durability = wear;
                    var tree = AddMoneyTree(f); var key = f.Launch(); double coins = f.C.Campaign.Coins;
                    for (int i = 0; i < 210; i++) f.Step(f.Hit(key, tree));
                    Near(coins + (wear == -1 ? 21 : wear) * 5, f.C.Campaign.Coins, "finite/infinite tree pays every full batch including the last use");
                    Near(wear == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "durability is per payout, not per entry");
                    Check(f.C.Marbles.ContainsKey(key), "depletion never consumes the ball");
                }
            using (var f = new Fixture())
            {
                var def = f.C.Balance.Devices.Single(d => d.Id == 9014); def.Value = 3; def.Strength = 7;
                var tree = AddMoneyTree(f); var key = f.Launch(); double coins = f.C.Campaign.Coins;
                for (int i = 0; i < 7; i++) f.Step(f.Hit(key, tree));
                Near(coins + 14, f.C.Campaign.Coins, "authoring parameters control threshold and reward");
                Check(f.Simulation.GetDeviceStatus(tree.InstanceId).Contains("1/3"), "custom progress shown");
                PiggyCoins(f, CampaignStateUtility.MaxCoins - 1);
                f.Step(f.Hit(key, tree)); f.Step(f.Hit(key, tree));
                Near(CampaignStateUtility.MaxCoins, f.C.Campaign.Coins, "reward respects wallet cap");
                var cash = AddCashOut(f); f.Step(f.Hit(key, cash));
                Check(!f.C.Marbles.ContainsKey(key), "counted ball qualifies as having triggered another device for CashOut");
            }
            foreach (bool invalidCount in new[] { false, true })
                using (var f = new Fixture())
                {
                    var def = f.C.Balance.Devices.Single(d => d.Id == 9014);
                    if (invalidCount) def.Value = 0; else def.Strength = 0;
                    var tree = AddMoneyTree(f); var key = f.Launch(); f.Step(f.Hit(key, tree));
                    Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "invalid parameters never consume durability");
                    Near(0, f.C.Manager.GetComponentData<BallFeatures>(f.Ball(key)).FirstTriggeredDeviceId, "invalid parameters do not register an effective trigger");
                }
            MoneyTreePhysics();
            Debug.Log("MONEY_TREE_VALIDATION_PASSED: " + (assertions - start) + " assertions; entry batches, coins, payout durability, independent rounds/instances, real pass-through, stay deduplication and fast sweeps.");
        }

        private static void MoneyTreePhysics()
        {
            foreach (float dt in new[] { .01f, .02f })
            {
                var scene = SceneManager.CreateScene("MoneyTreePhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var simulation = new MarbleSimulation(Balance()); int contacts = 0;
                var bridge = new MarblePhysicsBridge(scene, null, null, contact => { contacts++; simulation.EnqueueContact(contact); });
                var board = new GameObject("MoneyTreeBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
                try
                {
                    simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                    simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                    simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_MAX_SPEED, AttributeOperation.Set, 100);
                    var def = c.Balance.Devices.Single(d => d.Id == 9014); CampaignStateUtility.CreateDevice(c, def, def.Price);
                    var tree = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, tree.InstanceId, out var e);
                    var data = c.Manager.GetComponentData<OwnedDeviceData>(e); data.Placed = true; c.Manager.SetComponentData(e, data);
                    board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(63, 121, 0));
                    view = MResDeviceView.Create(tree, board.transform, c.DeviceScripts.Get(tree.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                    var colliders = view.Root.GetComponentsInChildren<Collider>(true).Where(c => c.name != "PlacementVolume").ToArray();
                    Check(colliders.Length == 1 && colliders.All(x => x.isTrigger), "entire tree has no solid collider");
                    Check(view.Root.transform.Find("Trunk") != null && view.Root.transform.Find("FruitCoin4") != null, "package supplies tree and coin fruit");
                    var spawn = board.transform.TransformPoint(new Vector3(0, .12f, .85f));
                    void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                    simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                    var key = c.Marbles.Keys.Single(); double coins = c.Campaign.Coins;
                    // Spawn overlapping the trigger, remain stationary, and verify stay reports do not accumulate.
                    Check(bridge.TryMove(key, board.transform.TransformPoint(new Vector3(0, .12f, 0)), Vector3.zero), "place ball inside tree");
                    for (int i = 0; i < 30; i++) Step();
                    Near(1, contacts, "enter/stay/sweep overlap reports count one continuous stay");
                    Check(simulation.GetDeviceStatus(tree.InstanceId).Contains("1/10"), "stationary stay adds one only");
                    Near(20, simulation.GetOwnedDevices().Single().RemainingDurability, "partial batch costs no durability");
                    for (int pass = 1; pass <= 19; pass++)
                    {
                        Check(bridge.TryMove(key, spawn, Vector3.zero), "place ball outside for reentry"); Step();
                        // Include fast crossings that can traverse the complete sensor within one gameplay tick.
                        float speed = pass % 2 == 0 ? 100 : 4;
                        var velocity = board.transform.rotation * Vector3.back * speed;
                        Check(bridge.TrySetVelocity(key, velocity), "approach tree without a Lua motion command");
                        for (int step = 0; step < 130; step++)
                        {
                            Step(); bridge.TryGetState(key, out var state);
                            if (board.transform.InverseTransformPoint(state.Position).z < -.85f) break;
                        }
                        Check(bridge.TryGetState(key, out var output), "ball survives the tree passage");
                        Check(board.transform.InverseTransformPoint(output.Position).z < -.85f, "ball passes through trunk and crown footprint");
                        VectorNear(velocity.normalized, output.LinearVelocity.normalized, "tree produces no deflection or reflection");
                        Check(output.LinearVelocity.magnitude > speed * .9f && output.LinearVelocity.magnitude <= speed + .001f, "passage has only existing natural damping");
                        Near(pass + 1, contacts, "each reentry counts once including high speed sweeps");
                        Near(coins + 5 * ((pass + 1) / 10), c.Campaign.Coins, "real traversal completes ten-entry batches");
                    }
                    Near(18, simulation.GetOwnedDevices().Single().RemainingDurability, "twenty real passages spend exactly two uses");
                    Near(0, c.Round.Score, "physical passages do not settle score");
                    Check(c.Marbles.Count == 1, "original ball remains in flight after every payout");
                }
                finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
            }
        }
    }
}
