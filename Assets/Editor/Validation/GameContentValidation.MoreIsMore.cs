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
        private static OwnedDeviceData AddMoreIsMore(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9023));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static int MoreCompanion(SimulationContext c, bool placed, bool exhausted = false)
        {
            var definition = c.Balance.Devices.Single(d => d.Id == 9008);
            CampaignStateUtility.CreateDevice(c, definition, definition.Price);
            int id = c.Campaign.NextDeviceId;
            CampaignStateUtility.FindDevice(c, id, out var entity);
            var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = placed;
            if (exhausted) { data.DurabilityRound = c.Round.RoundId; data.UsesThisRound = definition.Durability; }
            c.Manager.SetComponentData(entity, data); return id;
        }
        private static void MorePlace(SimulationContext c, int id, bool placed)
        {
            CampaignStateUtility.FindDevice(c, id, out var entity);
            var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = placed; c.Manager.SetComponentData(entity, data);
        }
        private static void MoreIsMoreLuaApi()
        {
            int start = assertions;
            foreach (int count in new[] { 0, 1, 2, 3, 9, 10, 11, 20 })
            using (var f = new Fixture())
            {
                var d = AddMoreIsMore(f); var key = f.Launch();
                for (int n = 0; n < count; n++) MoreCompanion(f.C, true, n % 2 == 0);
                for (int n = 0; n < 3; n++) MoreCompanion(f.C, false);
                var before = f.Physics.States[key]; double blood = f.C.Player.Blood, coins = f.C.Campaign.Coins;
                var hit = PiggyHit(f, key, d); f.Step(hit, hit);
                double bonus = Math.Min(20, count * 2);
                Near(1 + bonus, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "other placed devices add two each, capped at twenty per hit");
                Near(count == 0 ? 20 : 19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "zero bonus is free, positive bonus uses once");
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "device count does not change ball value");
                VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "Lua leaves normal bounce unchanged");
                Near(blood, f.C.Player.Blood, "count multiplier does not spend blood"); Near(coins, f.C.Campaign.Coins, "count multiplier does not spend coins");
                Near(0, f.C.Round.Score, "bonus waits for score area");
                Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("其他装置 " + count + " · 附加倍率 +" + bonus), "status reflects live count and bonus even for zero");
                f.Step(hit); Near(1 + 2 * bonus, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "repeat impacts add again; twenty is not a lifetime cap");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(10 * (1 + 2 * bonus), f.C.Round.Score + f.C.Round.FractionalScore, "score area applies final accumulated factor once");
            }
            MoreIsMoreLiveLayout(); MoreIsMoreLimits(); PlacedDeviceCountApi();
            foreach (float dt in new[] { .01f, .02f }) MoreIsMorePhysics(dt);
            Debug.Log("MORE_IS_MORE_VALIDATION_PASSED: " + (assertions - start) + " assertions; live placed counts, exclusion, caps, additive scoring, wear, API lifetime and real impacts.");
        }
        private static void MoreIsMoreLiveLayout()
        {
            using (var f = new Fixture())
            {
                var d = AddMoreIsMore(f); var a = f.Launch(); var b = f.Launch();
                var other = MoreCompanion(f.C, false);
                f.Step(PiggyHit(f, a, d)); Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "inventory device does not contribute");
                var query = CashApi(f, a, d); Check(!query.HasTriggeredOtherDevice, "no other device effect is invented"); query.Close();
                MorePlace(f.C, other, true); f.Step(); Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("+2"), "placing a device refreshes display without a collision");
                Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "layout change does not retroactively alter already flying balls");
                f.Step(PiggyHit(f, a, d), PiggyHit(f, b, d));
                Near(3, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "first simultaneous ball gets current bonus");
                Near(3, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(b)).ScoreMultiplier, "second simultaneous ball has no shared cooldown");
                Near(18, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "two distinct impacts spend two uses");
                MorePlace(f.C, other, false); f.Step(PiggyHit(f, a, d));
                Near(3, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "removing support stops new gains but keeps previous bonus");
                Near(18, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "zero bonus after retrieval is free");
                MorePlace(f.C, other, true); var exhausted = MoreCompanion(f.C, true, true);
                f.Step(PiggyHit(f, a, d)); Near(7, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "exhausted but placed companion still contributes");
                CampaignStateUtility.FindDevice(f.C, exhausted, out var removed); f.C.Manager.DestroyEntity(removed);
                f.Step(PiggyHit(f, a, d)); Near(9, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "deleted device immediately leaves the count");
                var ignored = PiggyHit(f, a, d); ignored.LinkKey = 88; f.Step(f.Hit(a, d), ignored);
                Near(16, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "trigger-only and link events cost nothing");
                var second = AddMoreIsMore(f); f.Step(PiggyHit(f, a, second));
                Near(13, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(a)).ScoreMultiplier, "second instance counts first and companion, excluding only itself");
                Near(19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == second.InstanceId).RemainingDurability, "second instance has independent wear");
                query = CashApi(f, a, d); Check(query.HasTriggeredOtherDevice, "positive bonus records successful effect history"); query.Close();
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "start another round with same placed layout");
                var fresh = f.Launch(); f.Step(PiggyHit(f, fresh, d));
                Near(5, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(fresh)).ScoreMultiplier, "fresh round uses current placement with no stale per-ball multiplier");
                Near(19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "new round restores full wear");
            }
            using (var f = new Fixture())
            {
                var d = AddMoreIsMore(f); var key = f.Launch(); MoreCompanion(f.C, true);
                var api = CashApi(f, key, d); api.SetDeviceScoreMultiplier(0); api.Close();
                f.Step(PiggyHit(f, key, d)); Near(2, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "plus two recovers from a zeroed device factor");
            }
        }
        private static void MoreIsMoreLimits()
        {
            foreach (int wear in new[] { 1, 20, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9023).Durability = wear;
                var d = AddMoreIsMore(f); var key = f.Launch(); MoreCompanion(f.C, true);
                for (int n = 0; n < 22; n++) f.Step(PiggyHit(f, key, d));
                int uses = wear == -1 ? 22 : wear;
                Near(1 + 2 * uses, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "last use applies completely and infinite durability continues");
                Near(wear == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "finite and infinite durability");
            }
            foreach (var config in new[] { (0d, 2f), (20d, 0f), (5.5d, 1.5f) })
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 9023); definition.Value = config.Item1; definition.Strength = config.Item2;
                var d = AddMoreIsMore(f); var key = f.Launch(); for (int n = 0; n < 4; n++) MoreCompanion(f.C, true);
                f.Step(PiggyHit(f, key, d)); double bonus = Math.Min(config.Item1, 4 * config.Item2);
                Near(1 + bonus, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "Lua uses configurable per-device gain and maximum, including fractional values");
                Near(bonus == 0 ? 20 : 19, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "disabled parameter has no effect or wear");
            }
        }
        private static void PlacedDeviceCountApi()
        {
            using (var f = new Fixture())
            {
                var d = AddMoreIsMore(f); var key = f.Launch(); var inventory = MoreCompanion(f.C, false); var placed = MoreCompanion(f.C, true, true);
                var api = CashApi(f, key, d); CampaignStateUtility.FindDevice(f.C, d.InstanceId, out var entity); var tick = new DeviceEventContext(f.C, entity);
                Near(2, api.CountPlacedDevices(), "default query includes caller and exhausted placed devices");
                Near(1, api.CountPlacedDevices(d.InstanceId), "Lua can exclude its own instance");
                Near(1, tick.CountPlacedDevices(placed), "event query can exclude any specified instance");
                Near(2, api.CountPlacedDevices(inventory), "excluding an unplaced device changes nothing");
                Near(2, tick.CountPlacedDevices(99999), "unknown exclusion is harmless");
                ExpectArgument(() => api.CountPlacedDevices(-1)); ExpectArgument(() => tick.CountPlacedDevices(-1));
                MorePlace(f.C, inventory, true); Near(3, api.CountPlacedDevices(), "live query sees placement in same callback");
                MorePlace(f.C, placed, false); Near(2, tick.CountPlacedDevices(), "live event query sees retrieval");
                CampaignStateUtility.FindDevice(f.C, inventory, out var removed); f.C.Manager.DestroyEntity(removed); Near(1, api.CountPlacedDevices(), "query sees entity deletion");
                // Paired purchases are two logical device instances, counted only when individually placed.
                var portal = f.C.Balance.Devices.Single(x => x.Id == 103); CampaignStateUtility.CreateDevice(f.C, portal, portal.Price);
                var pair = f.Simulation.GetOwnedDevices().Where(x => x.DefinitionId == 103).ToArray();
                Near(2, pair.Length, "portal purchase yields two independent instances");
                MorePlace(f.C, pair[0].InstanceId, true); Near(2, api.CountPlacedDevices(), "one placed portal counts as one");
                MorePlace(f.C, pair[1].InstanceId, true); Near(3, tick.CountPlacedDevices(), "both placed portals count as two");
                Near(20, api.RemainingDurability, "query does not spend wear"); Near(1, api.DeviceScoreMultiplier, "query does not mutate score");
                var flags = f.C.Manager.GetComponentData<BallFeatures>(f.Ball(key)); Near(0, flags.FirstTriggeredDeviceId, "read-only query does not mark an effect");
                api.Close(); tick.Close(); ExpectExpired(() => api.CountPlacedDevices()); ExpectExpired(() => tick.CountPlacedDevices());
            }
        }
        private static void MoreIsMorePhysics(float dt)
        {
            var scene = SceneManager.CreateScene("MoreIsMorePhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("MoreIsMoreBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 1);
                simulation.ApplyGlobalAttribute(995, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var def = c.Balance.Devices.Single(d => d.Id == 9023); CampaignStateUtility.CreateDevice(c, def, def.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>(); Check(collider != null && collider.convex && !collider.isTrigger, "more is more has solid cylindrical collision");
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
                    if (placed) { expected += 2; uses++; }
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
