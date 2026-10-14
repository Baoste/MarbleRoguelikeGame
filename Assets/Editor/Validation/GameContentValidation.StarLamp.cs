using System;
using System.Linq;
using MRes;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static OwnedDeviceData AddStarLamp(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9027));
            CampaignStateUtility.FindDevice(f.C, f.C.Campaign.NextDeviceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void LampSet(SimulationContext c, int instanceId, double value)
        { CampaignStateUtility.FindDevice(c, instanceId, out var entity); DeviceRoundState.Set(c, entity, "lit", value); }
        private static double LampState(SimulationContext c, int instanceId)
        { CampaignStateUtility.FindDevice(c, instanceId, out var entity); return DeviceRoundState.Get(c, entity, "lit"); }
        private static double LampMultiplier(Fixture f, MarbleKey key)
        { Check(f.Simulation.TryGetMarble(key, out var state), "lamp test ball exists"); return state.ScoreMultiplier; }
        private static double LampEnd(SimulationContext c, int id)
        { CampaignStateUtility.FindDevice(c, id, out var entity); return DeviceRoundState.Get(c, entity, "endsAt"); }
        private static void LampAdvance(Fixture f, double time)
        { for (int i = 0; f.C.Round.Time + 1e-7 < time && i < 1000; i++) f.Step(); }
        private static void StarLampLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                var a = AddStarLamp(f); var key = f.Launch(); var untouched = f.Launch();
                for (int i = 0; i < 4; i++)
                {
                    f.Step(f.Hit(key, a), f.Hit(key, a));
                    Near((i + 1) % 2, LampState(f.C, a.InstanceId), "one lamp still toggles once per entry");
                    Near(1, LampMultiplier(f, key), "one lamp cannot start global bonus");
                    Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "ordinary toggles are free");
                }
                var b = AddStarLamp(f); var c = AddStarLamp(f);
                f.Step(f.Hit(key, a)); f.Step(f.Hit(key, b));
                Near(1, LampMultiplier(f, untouched), "two out of three lit is insufficient");
                var link = f.Hit(key, c); link.LinkKey = 123; var impact = f.Hit(key, c); impact.HasImpact = true; impact.Normal = Vector3.up;
                f.Step(link, impact); Near(0, LampState(f.C, c.InstanceId), "invalid events cannot light last lamp");
                f.Step(f.Hit(key, c), f.Hit(key, c)); double end = LampEnd(f.C, a.InstanceId);
                Near(f.C.Round.Time + 5, end, "all lamps share a five-second window");
                Near(4, LampMultiplier(f, untouched), "ball that never touched any lamp receives board bonus");
                Near(4, LampMultiplier(f, key), "last switching ball also receives board bonus");
                Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "aura never enters permanent multiplier storage");
                var api = CashApi(f, untouched, a); Near(1, api.DeviceScoreMultiplier, "stored multiplier excludes temporary effects");
                Near(4, api.EffectiveDeviceScoreMultiplier, "effective query includes board contribution"); api.AddScoreMultiplier(1.5); api.Close();
                Near(4.5, LampMultiplier(f, untouched), "persistent and temporary contributions add rather than multiply");
                var fresh = f.Launch(); Near(4, LampMultiplier(f, fresh), "newly launched ball gains current board bonus");
                f.Step(f.Hit(key, a), f.Hit(untouched, b), f.Hit(fresh, c));
                foreach (var lamp in f.Simulation.GetOwnedDevices())
                { Near(1, LampState(f.C, lamp.InstanceId), "contacts during effect keep lights on"); Near(end, LampEnd(f.C, lamp.InstanceId), "contacts cannot refresh deadline"); Near(20, lamp.RemainingDurability, "no wear before expiration"); }
                Near(4, LampMultiplier(f, key), "reentries cannot stack the shared aura");
                f.Step(new MarbleContact { Key = fresh, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(40, f.C.Round.Score, "normal scoring during window uses temporary multiplier");
                LampAdvance(f, end - .1);
                foreach (var lamp in f.Simulation.GetOwnedDevices()) Near(20, lamp.RemainingDurability, "last step before deadline retains wear");
                f.Step();
                foreach (var lamp in f.Simulation.GetOwnedDevices())
                { Near(0, LampState(f.C, lamp.InstanceId), "all lamps extinguish together at timeout"); Near(19, lamp.RemainingDurability, "every participant pays exactly one"); }
                Near(1, LampMultiplier(f, key), "temporary bonus disappears even from existing ball");
                Near(1.5, LampMultiplier(f, untouched), "expiry preserves unrelated permanent multiplier");
                f.Step(); foreach (var lamp in f.Simulation.GetOwnedDevices()) Near(19, lamp.RemainingDurability, "completion cannot charge twice");
                f.Step(new MarbleContact { Key = untouched, Kind = MarbleContactKind.Score, TargetId = 701, Multiplier = 1, RushDisabled = true });
                Near(55, f.C.Round.Score, "score after deadline has no stale aura");
                f.Step(f.Hit(key, a)); f.Step(f.Hit(key, b)); f.Step(f.Hit(key, c));
                Near(4, LampMultiplier(f, key), "extinguished lamps can charge another window");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Check(f.Simulation.BeginRound(), "new round cancels active aura"); key = f.Launch();
                Near(1, LampMultiplier(f, key), "next round inherits no timed bonus");
                foreach (var lamp in f.Simulation.GetOwnedDevices())
                { Near(0, LampState(f.C, lamp.InstanceId), "next round starts off"); Near(20, lamp.RemainingDurability, "next round restores wear"); }
            }
            foreach (int durability in new[] { 1, 20, -1 })
                using (var f = new Fixture())
                {
                    var def = f.C.Balance.Devices.Single(d => d.Id == 9027); def.Durability = durability; def.Value = .3;
                    var a = AddStarLamp(f); var b = AddStarLamp(f); var key = f.Launch();
                    for (int cycle = 1; cycle <= 21; cycle++)
                    {
                        f.Step(f.Hit(key, a)); f.Step(f.Hit(key, b));
                        bool active = durability == -1 || cycle <= durability;
                        Near(active ? 3 : 1, LampMultiplier(f, key), "last durability supports complete next window");
                        double end = LampEnd(f.C, a.InstanceId); if (active) LampAdvance(f, end); else f.Step();
                        foreach (var lamp in f.Simulation.GetOwnedDevices())
                        { Near(0, LampState(f.C, lamp.InstanceId), "end of cycle is always off"); Near(durability == -1 ? -1 : Math.Max(0, durability - cycle), lamp.RemainingDurability, "each window costs one per participant, independent of hits"); }
                        Near(1, LampMultiplier(f, key), "window does not leave permanent score");
                    }
                }
            StarLampLifecycleAndScoring(); StarLampQueryApi(); StarLampVisibilityApi(); StarLampBoardApi(); StarLampDispatchApi();
            StarLampPhysics(.01f); StarLampPhysics(.02f);
            Debug.Log("STAR_LAMP_VALIDATION_PASSED: " + (assertions - start) + " assertions; all-lit board aura, five-second expiry, per-participant wear, fresh balls, additive settlement, lifecycle and real pass-through.");
        }
        private static void StarLampLifecycleAndScoring()
        {
            using (var f = new Fixture())
            {
                var a = AddStarLamp(f); var b = AddStarLamp(f); var inventory = AddStarLamp(f); MorePlace(f.C, inventory.InstanceId, false);
                MoreCompanion(f.C, true); var key = f.Launch(); var source = f.Launch();
                f.Step(f.Hit(source, a)); f.Step(f.Hit(source, b));
                Near(3, LampMultiplier(f, key), "inventory and different definitions do not block all-lit condition");
                Near(0, LampEnd(f.C, inventory.InstanceId), "unplaced lamp is not a participant");
                var api = CashApi(f, key, a); api.AddScoreMultiplier(2); api.Close();
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.RandomScore, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(40, f.C.Round.PendingScore, "gambling area includes aura and permanent bonus additively");
                var cash = AddCashOut(f); key = f.Launch(); double coins = f.C.Campaign.Coins;
                f.Step(f.Hit(key, cash)); Near(coins + 30, f.C.Campaign.Coins, "cashout uses live temporary multiplier for fresh ball");
                CampaignStateUtility.FindDevice(f.C, b.InstanceId, out var entity); f.C.Manager.DestroyEntity(entity);
                f.Step(); Near(0, LampState(f.C, a.InstanceId), "loss of aura source cancels remaining lamp window");
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == a.InstanceId).RemainingDurability, "cancelled window costs no wear");
                Near(1, LampMultiplier(f, source), "source removal immediately removes board contribution");
            }
        }
        private static void StarLampQueryApi()
        {
            using (var f = new Fixture())
            {
                var a = AddStarLamp(f); var b = AddStarLamp(f); var inventory = AddStarLamp(f); var key = f.Launch();
                MorePlace(f.C, inventory.InstanceId, false); LampSet(f.C, inventory.InstanceId, 1);
                int other = MoreCompanion(f.C, true); LampSet(f.C, other, 1);
                var api = CashApi(f, key, a); CampaignStateUtility.FindDevice(f.C, b.InstanceId, out var be);
                var tick = new DeviceEventContext(f.C, be);
                Near(9027, api.DefinitionId, "contact definition ID"); Near(9027, tick.DefinitionId, "event definition ID");
                Near(0, api.CountPlacedDevicesWithRoundValue(9027, "lit", 1), "different definitions and inventory ignored");
                Near(2, tick.CountPlacedDevicesWithRoundValue(9027, "lit", 0), "missing state means zero");
                api.SetRoundValue("lit", 1); tick.SetRoundValue("lit", 1);
                Near(2, api.CountPlacedDevicesWithRoundValue(api.DefinitionId, "lit", 1), "live writes visible immediately across instances");
                var data = f.C.Manager.GetComponentData<OwnedDeviceData>(be); data.DurabilityRound = f.C.Round.RoundId; data.UsesThisRound = 20; f.C.Manager.SetComponentData(be, data);
                Near(2, tick.CountPlacedDevicesWithRoundValue(9027, "lit", 1), "exhaustion never removes query membership");
                MorePlace(f.C, b.InstanceId, false); Near(1, api.CountPlacedDevicesWithRoundValue(9027, "lit", 1), "retrieval changes live count");
                MorePlace(f.C, b.InstanceId, true); Near(2, api.CountPlacedDevicesWithRoundValue(9027, "lit", 1), "same-round replacement keeps state");
                f.C.Manager.DestroyEntity(be); Near(1, api.CountPlacedDevicesWithRoundValue(9027, "lit", 1), "deleted device not counted");
                Near(1, api.CountPlacedDevicesWithRoundValue(9008, "lit", 1), "caller can query another definition");
                Near(0, api.CountPlacedDevicesWithRoundValue(uint.MaxValue, "lit", 1), "unknown definition returns zero");
                ExpectArgument(() => api.CountPlacedDevicesWithRoundValue(0, "lit", 1));
                ExpectArgument(() => api.CountPlacedDevicesWithRoundValue(9027, "", 1));
                ExpectArgument(() => api.CountPlacedDevicesWithRoundValue(9027, new string('x', 121), 1));
                ExpectArgument(() => api.CountPlacedDevicesWithRoundValue(9027, "lit", double.NaN));
                ExpectArgument(() => tick.CountPlacedDevicesWithRoundValue(9027, "lit", double.PositiveInfinity));
                Near(20, api.RemainingDurability, "queries and state writes do not spend wear");
                Near(0, f.C.Manager.GetComponentData<BallFeatures>(f.Ball(key)).FirstTriggeredDeviceId, "read-only query does not register gameplay effects");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Near(0, api.CountPlacedDevicesWithRoundValue(9027, "lit", 1), "build query targets upcoming round");
                api.SetRoundValue("lit", 1); Near(1, api.CountPlacedDevicesWithRoundValue(9027, "lit", 1), "build query can see prepared next-round state");
                api.Close(); tick.Close(); ExpectExpired(() => api.CountPlacedDevicesWithRoundValue(9027, "lit", 1));
                ExpectExpired(() => tick.CountPlacedDevicesWithRoundValue(9027, "lit", 1));
                ExpectExpired(() => { _ = api.DefinitionId; }); ExpectExpired(() => { _ = tick.DefinitionId; });
            }
        }
        private static void StarLampVisibilityApi()
        {
            var root = new GameObject("VisibilityProbe"); var child = new GameObject("Face"); child.transform.SetParent(root.transform, false);
            var nested = new GameObject("Nested"); nested.transform.SetParent(child.transform, false);
            var renderer = nested.AddComponent<MeshRenderer>(); var collider = child.AddComponent<BoxCollider>();
            var api = new MResLuaObject(root, null);
            try
            {
                Check(api.TrySetRenderersVisible("Face", false) && !renderer.enabled, "subtree renderers hidden");
                Check(child.activeSelf && nested.activeSelf && collider.enabled, "visibility does not alter activation or physics");
                Check(api.TrySetRenderersVisible("Face/Nested", true) && renderer.enabled, "relative descendant lookup");
                child.SetActive(false); Check(api.TrySetRenderersVisible("", false) && !renderer.enabled && !child.activeSelf, "inactive nodes retain activation state");
                Check(!api.TrySetRenderersVisible("Missing", true) && !api.TrySetRenderersVisible("../Face", true) && !api.TrySetRenderersVisible(null, true), "invalid or missing nodes fail without mutation");
                Check(!api.TrySetRenderersVisible("/Face", true) && !api.TrySetRenderersVisible("Face//Nested", true), "absolute and empty path segments rejected");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            Check(!api.TrySetRenderersVisible("Face", true), "destroyed presentation fails safely");
        }
    }
}
