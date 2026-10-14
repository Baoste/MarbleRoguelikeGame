using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static OwnedDeviceData AddGluttonBag(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9026));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void BagBuild(SimulationContext c)
        { var round = c.Round; round.Phase = (byte)RoundPhase.Build; c.Round = round; }
        private static int BagFood(SimulationContext c, DeviceRarity rarity = DeviceRarity.Common)
        {
            var definition = c.Balance.Devices.Single(d => d.Id == 9008); definition.Rarity = rarity;
            CampaignStateUtility.CreateDevice(c, definition, definition.Price); return c.Campaign.NextDeviceId;
        }
        private static void GluttonBagLuaApi()
        {
            int start = assertions;
            foreach (var rarity in new[] { DeviceRarity.Common, DeviceRarity.Rare, DeviceRarity.Epic, DeviceRarity.Legendary })
            using (var f = new Fixture())
            {
                var d = AddGluttonBag(f); var source = BagFood(f.C, rarity);
                Check(!f.Simulation.TryOfferDevice(source, d.InstanceId), "feeding during play is rejected");
                BagBuild(f.C); int revision = f.C.Campaign.LayoutRevision; double coins = f.C.Campaign.Coins;
                Check(f.Simulation.CanOfferDevice(source, d.InstanceId), "placed receiver advertises generic OnOffer");
                Check(f.Simulation.TryOfferDevice(source, d.InstanceId), "inventory offer commits");
                Check(!CampaignStateUtility.FindDevice(f.C, source, out _), "exactly the offered inventory instance is consumed");
                Near(revision + 1, f.C.Campaign.LayoutRevision, "offer refreshes inventory and placement projection once");
                Near(coins, f.C.Campaign.Coins, "feeding grants no sale coins");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "feeding does not consume durability");
                Check(!f.Simulation.TryOfferDevice(source, d.InstanceId), "same instance cannot be offered twice");
                double gain = 2 * ((int)rarity + 1);
                Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("成长 +" + gain), "build feedback shows upcoming round growth");
                Check(f.Simulation.BeginRound(), "begin prepared round"); var key = f.Launch();
                var before = f.Physics.States[key]; var hit = PiggyHit(f, key, d); f.Step(hit, hit);
                Near(1 + gain, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "rarity contributes two/four/six/eight actual multiplier points");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "same-step repeated report only spends one use");
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "base value remains intact until settlement");
                VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "Lua preserves normal bounce");
                Near(0, f.C.Round.Score, "feeding multiplier is deferred to scoring area");
                f.Step(hit); Near(1 + 2 * gain, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "later collision adds the growth again");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(10 * (1 + 2 * gain), f.C.Round.Score + f.C.Round.FractionalScore, "score area applies final accumulated factor");
            }
            BagRoundsAndLimits(); BagOfferContract(); BagRoundValueContract(); BagOfferUi();
            foreach (float dt in new[] { .01f, .02f }) GluttonBagPhysics(dt);
            Debug.Log("GLUTTON_BAG_VALIDATION_PASSED: " + (assertions - start) + " assertions; rarity feeding, atomic offers, round preparation, durability, drag UI and real collisions.");
        }
        private static void BagRoundsAndLimits()
        {
            using (var f = new Fixture())
            {
                var first = AddGluttonBag(f); var second = AddGluttonBag(f); var key = f.Launch();
                f.Step(PiggyHit(f, key, first)); Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "unfed bag is neutral");
                Near(0, f.C.Manager.GetComponentData<BallFeatures>(f.Ball(key)).FirstTriggeredDeviceId, "unfed contact is not a successful effect");
                BagBuild(f.C);
                for (int n = 0; n < 4; n++) Check(f.Simulation.TryOfferDevice(BagFood(f.C, (DeviceRarity)n), first.InstanceId), "multiple foods accumulate");
                Check(f.Simulation.TryOfferDevice(BagFood(f.C), second.InstanceId), "second receiver can grow independently");
                Check(f.Simulation.RemoveDevicePlacement(first.InstanceId), "receiver can be retrieved");
                MorePlace(f.C, first.InstanceId, true);
                Check(f.Simulation.BeginRound(), "prepared growth survives retrieval and start"); key = f.Launch(); var other = f.Launch();
                f.Step(PiggyHit(f, key, first), PiggyHit(f, other, first));
                Near(21, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "all four rarities sum to twenty");
                Near(21, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(other)).ScoreMultiplier, "simultaneous balls each get growth");
                f.Step(PiggyHit(f, key, second)); Near(23, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "second bag contributes only its own growth");
                BagBuild(f.C); Check(string.IsNullOrEmpty(f.Simulation.GetDeviceStatus(first.InstanceId)), "previous round status is not shown as new growth");
                Check(f.Simulation.TryOfferDevice(BagFood(f.C), second.InstanceId), "next build prepares fresh growth");
                Check(f.Simulation.BeginRound(), "begin next round"); key = f.Launch(); f.Step(PiggyHit(f, key, first));
                Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "unfed next round has zero growth");
                Near(20, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == first.InstanceId).RemainingDurability, "neutral new round consumes no restored durability");
                f.Step(PiggyHit(f, key, second)); Near(3, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "new food does not inherit previous round growth");
            }
            foreach (int durability in new[] { 1, 20, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9026).Durability = durability;
                var d = AddGluttonBag(f); BagBuild(f.C); Check(f.Simulation.TryOfferDevice(BagFood(f.C), d.InstanceId), "prepare wear test");
                Check(f.Simulation.BeginRound(), "start wear test"); var key = f.Launch();
                for (int n = 0; n < 22; n++) f.Step(PiggyHit(f, key, d));
                Near(1 + 2 * (durability == -1 ? 22 : durability), f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "finite and unlimited durability preserve last effect");
                Near(durability == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "feeding and impact wear are separate");
            }
        }
        private static void BagOfferContract()
        {
            using (var f = new Fixture())
            {
                var d = AddGluttonBag(f); var source = BagFood(f.C); BagBuild(f.C);
                Check(!f.Simulation.TryOfferDevice(d.InstanceId, d.InstanceId), "cannot consume self");
                Check(!f.Simulation.TryOfferDevice(-1, d.InstanceId) && !f.Simulation.TryOfferDevice(source, -1), "unknown inventory and receivers are rejected");
                MorePlace(f.C, d.InstanceId, false); Check(!f.Simulation.TryOfferDevice(source, d.InstanceId), "receiver must be placed"); MorePlace(f.C, d.InstanceId, true);
                MorePlace(f.C, source, true); Check(!f.Simulation.TryOfferDevice(source, d.InstanceId), "placed food is rejected");
                Check(!f.Simulation.TryOfferDevice(d.InstanceId, source), "normal device has no OnOffer callback"); MorePlace(f.C, source, false);
                var ordinary = BagFood(f.C); MorePlace(f.C, ordinary, true);
                Check(!f.Simulation.CanOfferDevice(source, ordinary) && !f.Simulation.TryOfferDevice(source, ordinary), "valid inventory cannot be offered to a target without OnOffer");
                CampaignStateUtility.FindDevice(f.C, d.InstanceId, out var entity); CampaignStateUtility.FindDevice(f.C, source, out var food);
                var api = new DeviceOfferContext(f.C, entity, food);
                Near(f.C.Round.RoundId + 1, api.RoundId, "build context targets upcoming round");
                Near(source, api.OfferedInstanceId, "offer identity"); Check(api.OfferedRarity == "common", "rarity uses metadata enum names");
                ExpectArgument(() => api.GetRoundValue("")); ExpectArgument(() => api.SetRoundValue("bad", double.NaN));
                ExpectArgument(() => api.SetRoundValue(new string('x', 121), 1));
                for (int n = 0; n < 32; n++) api.SetRoundValue("k" + n, n);
                bool full = false; try { api.SetRoundValue("overflow", 0); } catch (InvalidOperationException) { full = true; } Check(full, "per-device numeric state is bounded");
                Near(0, DeviceRoundState.Get(f.C, entity, "k1"), "offer writes are staged");
                api.Close(); ExpectExpired(() => api.Accept()); ExpectExpired(() => api.GetRoundValue("k1")); ExpectExpired(() => { var ignored = api.OfferedRarity; });
                var package = DeviceResourceCatalog.Current.Get(9026).Package;
                var probe = (MResLuaScript)Activator.CreateInstance(typeof(MResLuaScript), BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new object[] { @"local calls, saved = 0, nil
function OnOffer(a)
 calls, saved = calls+1, a
 a:SetRoundValue('bonus', 99); a:SetStatus('should only commit on acceptance')
 a:SetInstanceValue('growth', 12); a:SetDurabilityLimit(7); a:RestoreDurability()
 if calls == 1 then return end
 a:Accept(); a:Accept()
 if calls == 2 then error('EXPECTED_OFFER_FAILURE') end
end
function Verify() assert(not pcall(function() return saved.RoundId end)) end", package.Metadata, "OfferProbe", null }, null);
                var scripts = (Dictionary<int, MResLuaScript>)typeof(DeviceScriptSystem).GetField("scripts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.C.DeviceScripts);
                f.C.DeviceScripts.Remove(d.InstanceId); scripts.Add(d.InstanceId, probe);
                int revision = f.C.Campaign.LayoutRevision;
                for (int n = 0; n < 2; n++)
                {
                    Check(!f.Simulation.TryOfferDevice(source, d.InstanceId), "rejected and throwing callbacks abort offer");
                    Check(CampaignStateUtility.FindDevice(f.C, source, out _), "aborted offer retains source");
                    Near(0, DeviceRoundState.Get(f.C, entity, "bonus"), "aborted offer retains original growth");
                    Near(0, DeviceInstanceState.Get(f.C, entity, "growth"), "aborted offer retains persistent state");
                    Near(20, DeviceDurabilitySystem.Limit(f.C, f.C.Manager.GetComponentData<OwnedDeviceData>(entity)), "aborted offer retains capacity");
                    Near(revision, f.C.Campaign.LayoutRevision, "aborted offer does not change inventory revision");
                    Check(string.IsNullOrEmpty(f.Simulation.GetDeviceStatus(d.InstanceId)), "aborted status is not published"); probe.Invoke("Verify");
                }
                Check(f.Simulation.TryOfferDevice(source, d.InstanceId), "successful retry commits exactly once despite duplicate Accept");
                Near(99, DeviceRoundState.Get(f.C, entity, "bonus"), "accepted growth committed");
                Near(12, DeviceInstanceState.Get(f.C, entity, "growth"), "accepted persistent growth committed");
                Near(7, DeviceDurabilitySystem.Remaining(f.C, f.C.Manager.GetComponentData<OwnedDeviceData>(entity)), "accepted capacity and refill committed");
                var portal = f.C.Balance.Devices.Single(x => x.Id == 103); CampaignStateUtility.CreateDevice(f.C, portal, portal.Price);
                var pair = f.Simulation.GetOwnedDevices().Where(x => x.DefinitionId == 103).ToArray();
                Check(f.Simulation.TryOfferDevice(pair[0].InstanceId, d.InstanceId), "one member of a paired purchase can be offered");
                Check(CampaignStateUtility.FindDevice(f.C, pair[1].InstanceId, out _), "unoffered paired member remains owned");
                Check(f.Simulation.SellDevice(d.InstanceId), "receiver can be sold");
                Check(!CampaignStateUtility.FindDevice(f.C, d.InstanceId, out _), "receiver state is removed with its entity");
            }
        }
        private static void BagRoundValueContract()
        {
            using (var f = new Fixture())
            {
                var d = AddGluttonBag(f); var second = AddGluttonBag(f); var key = f.Launch();
                CampaignStateUtility.FindDevice(f.C, d.InstanceId, out var entity);
                var contact = CashApi(f, key, d); var tick = new DeviceEventContext(f.C, entity);
                Near(0, contact.GetRoundValue("custom"), "missing round value is zero");
                contact.SetRoundValue("custom", 3.5); Near(3.5, tick.GetRoundValue("custom"), "event callback reads state written by contact");
                tick.SetRoundValue("custom", -2); Near(-2, contact.GetRoundValue("custom"), "numeric state supports finite negative and fractional values");
                Near(0, f.C.Manager.GetComponentData<BallFeatures>(f.Ball(key)).FirstTriggeredDeviceId, "state API alone does not mark ball effects");
                Near(20, contact.RemainingDurability, "state API alone does not consume durability");
                var other = CashApi(f, key, second); Near(0, other.GetRoundValue("custom"), "state is isolated by device instance"); other.Close();
                ExpectArgument(() => tick.SetRoundValue("custom", double.PositiveInfinity));
                ExpectArgument(() => contact.GetRoundValue(null));
                contact.Close(); tick.Close();
                ExpectExpired(() => contact.GetRoundValue("custom")); ExpectExpired(() => tick.SetRoundValue("custom", 1));
                BagBuild(f.C); Check(f.Simulation.BeginRound(), "advance round for generic state test"); key = f.Launch();
                contact = CashApi(f, key, d); Near(0, contact.GetRoundValue("custom"), "previous round state is no longer visible"); contact.Close();
            }
        }
        private static void GluttonBagPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("GluttonBagPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("GluttonBagBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(993, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(994, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 1);
                simulation.ApplyGlobalAttribute(995, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var def = c.Balance.Devices.Single(d => d.Id == 9026); CampaignStateUtility.CreateDevice(c, def, def.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                BagBuild(c); Check(simulation.TryOfferDevice(BagFood(c), owned.InstanceId), "prepare physical bag by consuming common inventory");
                Check(simulation.BeginRound(), "start physical prepared round");
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>(); Check(collider != null && collider.convex && !collider.isTrigger, "glutton bag has solid cylindrical collision");
                var spawn = board.transform.TransformPoint(new Vector3(0, .1f, .8f)); var incoming = board.transform.rotation * Vector3.back * 2;
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0; double expected = 1; int uses = 0;
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                for (int impact = 0; impact < 3; impact++)
                {
                    Check(bridge.TryMove(key, spawn, Vector3.zero), "leave previous physical contact"); Step();
                    int before = hits.Count; Check(bridge.TrySetVelocity(key, incoming), "approach solid surface"); Vector3 solved = default;
                    for (int n = 0; hits.Count == before && n < 100; n++)
                    { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); solved = body.velocity; simulation.AfterPhysics(bridge); }
                    Check(hits.Count == before + 1, "real collision enters once");
                    expected += 2; uses++;
                    Near(expected, c.Manager.GetComponentData<MarbleScore>(c.Marbles[key]).ScoreMultiplier, "prepared growth adds two per real impact");
                    Near(10, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "physical ball retains value");
                    Check(Vector3.Dot(body.velocity, incoming) < 0, "ball bounces normally"); VectorNear(solved, body.velocity, "Lua preserves solver velocity");
                    for (int n = 0; n < 5; n++) Step(); Check(hits.Count == before + 1, "continuous contact does not reapply bonus");
                    Near(20 - uses, simulation.GetOwnedDevices().Single(x => x.InstanceId == owned.InstanceId).RemainingDurability, "physical effective-use durability");
                }
                simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true }); Step();
                Near(70, c.Round.Score, "three plus-two effects settle as value times seven");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
