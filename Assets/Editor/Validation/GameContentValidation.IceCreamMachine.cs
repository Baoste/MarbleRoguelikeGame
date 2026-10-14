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
        private static OwnedDeviceData AddIceCreamMachine(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9025));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void IceCreamMachineLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                var d = AddIceCreamMachine(f); var key = f.Launch();
                var before = f.Physics.States[key]; double blood = f.C.Player.Blood, coins = f.C.Campaign.Coins;
                var hit = PiggyHit(f, key, d); double expected = 1;
                f.Step(); Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("已触发 0 次 · 下次 ×10"), "new machine advertises its first full factor");
                for (int i = 0; i < 12; i++)
                {
                    f.Step(hit, hit); expected += Math.Max(0, 9 - i);
                    Near(expected, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "factors ten through two add factor minus one, then remain neutral");
                    Near(20 - Math.Min(9, i + 1), f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "same-step duplicate counts once; nine effective hits use nine durability");
                    string next = "已触发 " + Math.Min(9, i + 1) + " 次 · 下次 ×" + Math.Max(1, 9 - i);
                    Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains(next), "display shows successful count and next full multiplier");
                    Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "ice cream multiplier leaves base value unchanged");
                    Near(0, f.C.Round.Score, "multipliers wait for score-area settlement");
                }
                Near(46, expected, "nine bonuses sum to forty-five above initial one");
                VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "Lua preserves bounce velocity");
                Near(blood, f.C.Player.Blood, "machine does not spend blood"); Near(coins, f.C.Campaign.Coins, "machine does not spend coins");
                var unused = f.Launch(); f.Step(PiggyHit(f, unused, d));
                Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(unused)).ScoreMultiplier, "new balls also receive no bonus after the machine is used up");
                Near(0, f.C.Manager.GetComponentData<BallFeatures>(f.Ball(unused)).FirstTriggeredDeviceId, "neutral hit does not record a successful device effect");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(460, f.C.Round.Score + f.C.Round.FractionalScore, "settlement uses accumulated forty-six factor once");
            }
            IceCreamMachineInstances(); IceCreamMachineLimits();
            foreach (float dt in new[] { .01f, .02f }) IceCreamMachinePhysics(dt);
            Debug.Log("ICE_CREAM_MACHINE_VALIDATION_PASSED: " + (assertions - start) + " assertions; decreasing factors, per-instance rounds, duplicate contacts, additive settlement, durability and real collisions.");
        }
        private static void IceCreamMachineInstances()
        {
            using (var f = new Fixture())
            {
                var first = AddIceCreamMachine(f); var second = AddIceCreamMachine(f); var a = f.Launch(); var b = f.Launch();
                var ignored = PiggyHit(f, a, first); ignored.LinkKey = 88; f.Step(f.Hit(a, first), ignored);
                Near(20, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == first.InstanceId).RemainingDurability, "trigger and link events do not advance machine or wear");
                f.Step(PiggyHit(f, a, first), PiggyHit(f, b, first));
                var factors = new[] { f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(b)).ScoreMultiplier }.OrderBy(x => x).ToArray();
                Near(9, factors[0], "second of simultaneous balls gets next multiplier"); Near(10, factors[1], "first of simultaneous balls gets initial multiplier");
                Near(18, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == first.InstanceId).RemainingDurability, "distinct balls in one step consume two uses");
                double previous = f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier;
                f.Step(PiggyHit(f, a, second));
                Near(previous + 9, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "another instance starts at ten independently");
                Near(19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == second.InstanceId).RemainingDurability, "independent instance durability");
                var api = CashApi(f, a, first); Check(api.HasTriggeredOtherDevice, "successful second instance records effect history"); api.Close();
                MorePlace(f.C, first.InstanceId, false); f.Step(); MorePlace(f.C, first.InstanceId, true); f.Step();
                Check(f.Simulation.GetDeviceStatus(first.InstanceId).Contains("下次 ×8"), "retrieval and replacement within a round do not refill machine");
                previous = f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier;
                f.Step(PiggyHit(f, a, first)); Near(previous + 7, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "next effective hit continues at eight");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "start next round");
                var fresh = f.Launch(); f.Step(PiggyHit(f, fresh, first));
                Near(10, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(fresh)).ScoreMultiplier, "new round restarts at ten");
                Near(19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == first.InstanceId).RemainingDurability, "new round restores durability");
                Check(f.Simulation.GetDeviceStatus(second.InstanceId).Contains("已触发 0 次 · 下次 ×10"), "untouched second instance also resets on new-round tick");
            }
            using (var f = new Fixture())
            {
                var d = AddIceCreamMachine(f); var key = f.Launch();
                var api = CashApi(f, key, d); api.SetDeviceScoreMultiplier(0); api.Close();
                f.Step(PiggyHit(f, key, d)); Near(9, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "first bonus adds nine even to a previously zeroed multiplier");
            }
        }
        private static void IceCreamMachineLimits()
        {
            foreach (int wear in new[] { 1, 5, 20, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9025).Durability = wear;
                var d = AddIceCreamMachine(f); var key = f.Launch();
                for (int n = 0; n < 12; n++) f.Step(PiggyHit(f, key, d));
                int uses = wear == -1 ? 9 : Math.Min(9, wear);
                Near(1 + uses * 9 - uses * (uses - 1) / 2, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "last durability grants full factor, infinite durability still respects decay");
                Near(wear == -1 ? -1 : wear - uses, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "only effective hits use durability");
            }
            foreach (var config in new[] { (1d, 1f, 1d, 0), (0d, 1f, 1d, 0), (10d, 0f, 37d, 4), (2.5d, .75f, 3.25d, 2), (3d, 5f, 3d, 1) })
            using (var f = new Fixture())
            {
                var def = f.C.Balance.Devices.Single(d => d.Id == 9025); def.Value = config.Item1; def.Strength = config.Item2;
                var d = AddIceCreamMachine(f); var key = f.Launch();
                for (int n = 0; n < 4; n++) f.Step(PiggyHit(f, key, d));
                Near(config.Item3, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "zero reduction, fractional values and floor overshoot have defined behavior");
                Near(20 - config.Item4, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "neutral configurations do not use durability");
            }
        }
        private static void IceCreamMachinePhysics(float dt)
        {
            var scene = SceneManager.CreateScene("IceCreamMachinePhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("IceCreamMachineBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 1);
                simulation.ApplyGlobalAttribute(995, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var def = c.Balance.Devices.Single(d => d.Id == 9025); CampaignStateUtility.CreateDevice(c, def, def.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>(); Check(collider != null && collider.convex && !collider.isTrigger, "ice cream machine has solid cylindrical collision");
                var spawn = board.transform.TransformPoint(new Vector3(0, .1f, .8f)); var incoming = board.transform.rotation * Vector3.back * 2;
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0; double expected = 1; int uses = 0;
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                for (int impact = 0; impact < 11; impact++)
                {
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "leave previous physical contact"); Step();
                    int before = hits.Count; Check(bridge.TrySetVelocity(key, incoming), "approach solid surface"); Vector3 solved = default;
                    for (int n = 0; hits.Count == before && n < 100; n++)
                    { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); solved = body.velocity; simulation.AfterPhysics(bridge); }
                    Check(hits.Count == before + 1, "real collision enters once");
                    if (impact < 9) { expected += 9 - impact; uses++; }
                    Near(expected, c.Manager.GetComponentData<MarbleScore>(c.Marbles[key]).ScoreMultiplier, "each real re-entry takes the next multiplier until the neutral floor");
                    Near(10, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "physical ball retains value");
                    Check(Vector3.Dot(body.velocity, incoming) < 0, "ball bounces normally"); VectorNear(solved, body.velocity, "Lua preserves solver velocity");
                    for (int n = 0; n < 5; n++) Step(); Check(hits.Count == before + 1, "continuous contact does not reapply bonus");
                    Near(20 - uses, simulation.GetOwnedDevices().Single(x => x.InstanceId == owned.InstanceId).RemainingDurability, "physical effective-use durability");
                }
                simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true }); Step();
                Near(460, c.Round.Score, "nine declining bonuses settle as value times forty-six");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
