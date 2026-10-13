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
        private static OwnedDeviceData AddCashOut(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9013));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static DeviceContactContext CashApi(Fixture f, MarbleKey key, OwnedDeviceData device)
        {
            CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var entity);
            return new DeviceContactContext(f.C, f.Ball(key), entity, f.Hit(key, device), f.Physics.Poses[device.InstanceId]);
        }
        private static void CashOutLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                var cash = AddCashOut(f); var key = f.Launch(); double coins = f.C.Campaign.Coins;
                float speed = f.Physics.States[key].LinearVelocity.magnitude;
                f.Step(f.Hit(key, cash), f.Hit(key, cash));
                Near(1, f.Physics.Moves, "duplicate entry launches once");
                VectorNear(Vector3.forward * speed, f.Physics.States[key].LinearVelocity, "fresh ball exits board-down at entry speed");
                Near(coins, f.C.Campaign.Coins, "fresh ball gives no coins");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "successful pass spends one use");
                var own = CashApi(f, key, cash);
                Check(!own.HasTriggeredOtherDevice, "this device's prior successful pass does not count as another instance"); own.Close();
                f.Step(f.Hit(key, cash)); Check(f.C.Marbles.ContainsKey(key), "self reentry without another effect still passes");
                Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "self reentry is another successful pass");
                var otherCash = AddCashOut(f); f.Step(f.Hit(key, otherCash));
                Check(!f.C.Marbles.ContainsKey(key), "another instance of the same definition counts as other");
                Near(coins + 10, f.C.Campaign.Coins, "value ten pays ten, without the removed times-two factor");
                Near(0, f.C.Round.Score, "redemption never adds round score");
            }
            using (var f = new Fixture())
            {
                var pig = AddPiggyBank(f); var cash = AddCashOut(f); var key = f.Launch(); PiggyCoins(f, 50);
                f.Step(PiggyHit(f, key, pig)); f.Step(PiggyHit(f, key, pig));
                var api = CashApi(f, key, cash);
                Check(api.HasTriggeredOtherDevice, "successful multiplier impacts are recorded");
                Near(2, api.DeviceScoreMultiplier, "two pig factors of 1.5 give accumulated factor two");
                api.Close(); GrantRefund(f, key); double blood = f.C.Player.Blood;
                f.Step(f.Hit(key, cash), new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 1, Multiplier = 1 });
                Near(70, f.C.Campaign.Coins, "ten value with factor two returns twenty coins");
                Near(0, f.C.Round.Score, "same-step score contact cannot settle the redeemed ball again");
                Near(blood, f.C.Player.Blood, "consumption does not refund launch blood");
                Check(!f.C.Marbles.ContainsKey(key), "redeemed ball removed from simulation and physics");
                Check(!f.Physics.States.ContainsKey(key), "redeemed physical body removed");
                ExpectExpired(() => { _ = api.HasTriggeredOtherDevice; });
                ExpectExpired(() => { _ = api.DeviceScoreMultiplier; }); ExpectExpired(() => api.MarkTriggered());
            }
            foreach (double value in new[] { 0, .24, .25, .49, .5, 1.5, 10.49, 10.5 })
                foreach (double multiplier in new[] { 1d, 2d })
                    using (var f = new Fixture())
                    {
                        var other = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9000)); var cash = AddCashOut(f); var key = f.Launch();
                        var api = CashApi(f, key, other); api.Set("BALL_BASE_VALUE", value); api.AddScoreMultiplier(multiplier); api.MarkTriggered(); api.Close();
                        double coins = f.C.Campaign.Coins;
                        f.Step(f.Hit(key, cash), f.Hit(key, cash));
                        Near(coins + Math.Floor(value * multiplier + .5), f.C.Campaign.Coins, "round half-up after multiplying value, once per ball");
                        Check(!f.C.Marbles.ContainsKey(key), "even a zero-value eligible ball is consumed");
                    }
            CashOutHistory(); CashOutWear(); CashOutPhysics();
            Debug.Log("CASH_OUT_VALIDATION_PASSED: " + (assertions - start) + " assertions; effective history, factor-aware rounded coins, self exclusion, durability, lifecycle and real trigger/field physics.");
        }
        private static void CashOutHistory()
        {
            using (var f = new Fixture())
            {
                var other = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9000)); var cash = AddCashOut(f); var key = f.Launch();
                var api = CashApi(f, key, other); var query = CashApi(f, key, cash);
                api.Visit(-88, 0); api.TryUse(); api.SetStatus("visited"); api.AddScoreMultiplier(1); api.AddBase("BALL_BASE_VALUE", 0);
                Check(!query.HasTriggeredOtherDevice, "visits, durability, status and neutral contributions do not mean an effective action");
                Check(!api.Motion.TrySetVelocity(new Vector3(float.NaN, 0, 0)), "invalid velocity rejected");
                f.Physics.BlockMoves = true; f.Step(f.Hit(key, other));
                Check(!query.HasTriggeredOtherDevice, "failed movement after accepted trigger does not record a successful effect");
                f.Step(f.Hit(key, cash));
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == cash.InstanceId).RemainingDurability, "blocked cash exit costs nothing");
                Check(!f.C.Manager.GetComponentData<BallFeatures>(f.Ball(key)).TriggeredMultipleDevices, "failed operations leave history empty");
                f.Physics.BlockMoves = false; f.Step(f.Hit(key, cash));
                Check(f.C.Marbles.ContainsKey(key), "visit-only ball is passed through");
                api.MarkTriggered(); api.MarkTriggered();
                Check(query.HasTriggeredOtherDevice && api.HasTriggeredOtherDevice, "two distinct successes remembered from either instance");
                f.Simulation.ApplyMarbleEffect(key, MarbleModifierAttribute.ScoreMul, 3);
                Near(1, query.DeviceScoreMultiplier, "independent permanent score multiplier is not an accumulated device factor");
                Check(ContentSpawnSystem.Children(f.C, f.Ball(key), 1, false, Vector3.forward, Vector3.up), "copy previously affected ball");
                var child = f.C.Marbles.Keys.Single(k => !k.Equals(key)); var childApi = CashApi(f, child, cash);
                Check(childApi.HasTriggeredOtherDevice, "copy inherits successful device history"); childApi.Close();
                var fresh = f.Launch(); var freshApi = CashApi(f, fresh, cash);
                Check(!freshApi.HasTriggeredOtherDevice, "separate new ball starts with empty history"); freshApi.Close();
                CampaignStateUtility.FindDevice(f.C, other.InstanceId, out var entity); f.C.Manager.DestroyEntity(entity);
                Check(query.HasTriggeredOtherDevice, "history survives removal of the other device");
                api.Close(); query.Close();
            }
            using (var f = new Fixture())
            {
                var pig = AddPiggyBank(f); var cash = AddCashOut(f); var key = f.Launch(); PiggyCoins(f, 9);
                f.Step(PiggyHit(f, key, pig)); f.Step(f.Hit(key, cash));
                Check(f.C.Marbles.ContainsKey(key), "inactive pig with fewer than ten coins is not an effective trigger");
                var stone = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9009));
                var hit = f.Hit(key, stone); hit.HasImpact = true; hit.Normal = Vector3.back; hit.IncomingVelocity = Vector3.forward;
                f.Step(hit); var api = CashApi(f, key, cash);
                Check(api.HasTriggeredOtherDevice, "successful Lua-only charging is explicitly recorded before release"); api.Close();
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == stone.InstanceId).RemainingDurability, "recording a charge does not spend durability");
                f.Step(f.Hit(key, cash)); Near(19, f.C.Campaign.Coins, "charged ball can be redeemed");
            }
            using (var f = new Fixture())
            {
                var mine = AddGoldMine(f); var cash = AddCashOut(f); var key = f.Launch(); double coins = f.C.Campaign.Coins;
                f.Step(GoldMineHit(f, key, mine), f.Hit(key, cash));
                Near(coins + 4 + 8, f.C.Campaign.Coins, "earlier successful contact in the same step counts and current value is used");
            }
        }
        private static void CashOutWear()
        {
            foreach (int wear in new[] { 1, 20, -1 })
                using (var f = new Fixture())
                {
                    f.C.Balance.Devices.Single(d => d.Id == 9013).Durability = wear;
                    var other = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9000)); var cash = AddCashOut(f);
                    double coins = f.C.Campaign.Coins;
                    for (int i = 0; i < 22; i++)
                    {
                        var key = f.Launch(); var api = CashApi(f, key, other); api.MarkTriggered(); api.Close(); f.Step(f.Hit(key, cash));
                        Check(f.C.Marbles.ContainsKey(key) == (wear >= 0 && i >= wear), "last use processes fully; depleted device stops processing");
                    }
                    Near(coins + 10 * (wear == -1 ? 22 : wear), f.C.Campaign.Coins, "finite and infinite payouts");
                    Near(wear == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == cash.InstanceId).RemainingDurability, "one durability per successful redemption");
                    var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                    Check(f.Simulation.BeginRound(), "begin another round"); var fresh = f.Launch(); var query = CashApi(f, fresh, cash);
                    Check(!query.HasTriggeredOtherDevice, "new round cannot retain earlier ball history"); query.Close();
                    f.Step(f.Hit(fresh, cash));
                    Near(wear == -1 ? -1 : wear - 1, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == cash.InstanceId).RemainingDurability, "reset restores durability and fresh pass costs one");
                }
            using (var f = new Fixture())
            {
                var other = AddPiggyBank(f); var cash = AddCashOut(f); var key = f.Launch();
                PiggyCoins(f, CampaignStateUtility.MaxCoins - 1); var api = CashApi(f, key, other); api.MarkTriggered(); api.Close();
                f.Step(f.Hit(key, cash)); Near(CampaignStateUtility.MaxCoins, f.C.Campaign.Coins, "coin reward respects wallet cap");
            }
        }
        private static void CashOutPhysics()
        {
            foreach (bool field in new[] { false, true })
            {
                var scene = SceneManager.CreateScene("CashOutPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var simulation = new MarbleSimulation(Balance());
                var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact);
                var board = new GameObject("CashOutBoard"); SceneManager.MoveGameObjectToScene(board, scene);
                MResLoadHandle view = null, fieldView = null;
                try
                {
                    simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                    simulation.ApplyGlobalAttribute(989, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                    board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                    OwnedDeviceSnapshot AddView(uint id)
                    {
                        var def = c.Balance.Devices.Single(d => d.Id == id); CampaignStateUtility.CreateDevice(c, def, def.Price);
                        var owned = simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var e);
                        var data = c.Manager.GetComponentData<OwnedDeviceData>(e); data.Placed = true; c.Manager.SetComponentData(e, data); return owned;
                    }
                    var cash = AddView(9013); view = MResDeviceView.Create(cash, board.transform, c.DeviceScripts.Get(cash.InstanceId, 9013)); view.Root.SetActive(true);
                    Check(view.Root.GetComponent<BoxCollider>().isTrigger, "exchange gate has an entrance trigger");
                    var spawn = board.transform.TransformPoint(new Vector3(0, .1f, .8f));
                    simulation.FireOnce(); simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                    var key = c.Marbles.Keys.Single(); double coins = c.Campaign.Coins;
                    if (field)
                    {
                        var ownedField = AddView(9004); fieldView = MResDeviceView.Create(ownedField, board.transform, c.DeviceScripts.Get(ownedField.InstanceId, 9004));
                        fieldView.Root.transform.localPosition = new Vector3(2, 0, 0); fieldView.Root.SetActive(true); Physics.SyncTransforms();
                        Check(bridge.TryMove(key, board.transform.TransformPoint(new Vector3(2, .1f, 0)), Vector3.zero), "place inside separate force region");
                        simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge);
                        Near(ownedField.InstanceId, c.Manager.GetComponentData<BallFeatures>(c.Marbles[key]).FirstTriggeredDeviceId, "nonzero field force records its source without a collision");
                        fieldView.Root.SetActive(false);
                    }
                    Check(bridge.TryMove(key, spawn, board.transform.rotation * Vector3.back * 2), "approach exchange gate");
                    Physics.SyncTransforms();
                    float entrySpeed = 0;
                    for (int i = 0; i < 45 && simulation.GetOwnedDevices().Single(d => d.InstanceId == cash.InstanceId).RemainingDurability == 20; i++)
                    {
                        simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f);
                        if (bridge.TryGetState(key, out var incoming)) entrySpeed = incoming.LinearVelocity.magnitude;
                        simulation.AfterPhysics(bridge);
                    }
                    Near(19, simulation.GetOwnedDevices().Single(d => d.InstanceId == cash.InstanceId).RemainingDurability, "physical entrance executes one complete packaged effect");
                    if (field)
                    { Check(!c.Marbles.ContainsKey(key), "field-affected ball is physically removed"); Near(coins + 10, c.Campaign.Coins, "field history enables redemption"); }
                    else
                    {
                        Check(bridge.TryGetState(key, out var state), "fresh ball still has physical state");
                        VectorNear(Vector3.ProjectOnPlane(Vector3.down, board.transform.up).normalized * entrySpeed, state.LinearVelocity, "real rotated-board exit preserves entry magnitude and points downhill");
                        Check(Vector3.Dot(state.Position - view.Root.transform.position, state.LinearVelocity.normalized) > .32f, "launch positions ball outside the lower edge");
                        Near(coins, c.Campaign.Coins, "physical fresh pass gives no reward");
                    }
                    // Reuse a pooled body after removal: history belongs to the entity, not the view.
                    simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Drain, TargetId = 1 });
                    simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                    Check(c.Marbles.Count == 0, "remove the previous ball before testing pool reuse");
                    simulation.FireOnce();
                    for (int i = 0; i < 3; i++) { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); }
                    var fresh = c.Marbles.Keys.Single(); Near(0, c.Manager.GetComponentData<BallFeatures>(c.Marbles[fresh]).FirstTriggeredDeviceId, "reused physical body starts with fresh trigger history");
                }
                finally { fieldView?.Dispose(); view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
            }
        }
    }
}
