using System;
using System.Linq;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static int DentalFood(SimulationContext c)
        {
            var definition = c.Balance.Devices.Single(d => d.Id == 9028);
            CampaignStateUtility.CreateDevice(c, definition, definition.Price); return c.Campaign.NextDeviceId;
        }
        private static OwnedDeviceData AddDental(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9028));
            CampaignStateUtility.FindDevice(f.C, f.C.Campaign.NextDeviceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void DentalImplantLuaApi()
        {
            int start = assertions;
            var limits = new[] { 0, 5, 8, 20 }; var bonuses = new[] { 0, 2, 5, 20 };
            for (int stage = 0; stage <= 3; stage++)
            using (var f = new Fixture())
            {
                var d = AddDental(f); CampaignStateUtility.FindDevice(f.C, d.InstanceId, out var entity);
                Near(0, f.Simulation.GetOwnedDevices().Single().Durability, "created instance is a dormant hole");
                BagBuild(f.C);
                for (int upgrade = 1; upgrade <= stage; upgrade++)
                {
                    int food = DentalFood(f.C); double coins = f.C.Campaign.Coins;
                    Check(f.Simulation.TryOfferDevice(food, d.InstanceId), "same-type inventory advances one stage");
                    Check(!CampaignStateUtility.FindDevice(f.C, food, out _), "upgrade consumes exactly one item");
                    Near(coins, f.C.Campaign.Coins, "upgrade does not award sale coins");
                    Near(upgrade, DeviceInstanceState.Get(f.C, entity, "stage"), "stage is persisted on receiver");
                    Near(limits[upgrade], f.Simulation.GetOwnedDevices().Single().RemainingDurability, "upgrade fills stage capacity");
                }
                Check(f.Simulation.BeginRound(), "begin tooth round"); var key = f.Launch();
                var hit = PiggyHit(f, key, d); var velocity = f.Physics.States[key].LinearVelocity;
                if (stage == 0)
                {
                    f.Step(hit); Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "hole grants no effect");
                    Near(0, f.C.Manager.GetComponentData<BallFeatures>(f.Ball(key)).FirstTriggeredDeviceId, "hole does not count as successful device");
                    Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("挖洞"), "dormant status remains readable during play");
                    continue;
                }
                f.Step(f.Hit(key, d)); Near(limits[stage], f.Simulation.GetOwnedDevices().Single().RemainingDurability, "non-impact sensor report does not spend a tooth use");
                f.Step(hit, hit);
                Near(1 + bonuses[stage], f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "one impact adds actual bonus once");
                Near(limits[stage] - 1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "duplicate same-step report costs one use");
                VectorNear(velocity, f.Physics.States[key].LinearVelocity, "tooth preserves solver velocity");
                Near(0, f.C.Round.Score, "score awaits scoring area");
                BagBuild(f.C); Check(f.Simulation.RemoveDevicePlacement(d.InstanceId), "retrieve grown tooth"); MorePlace(f.C, d.InstanceId, true);
                Check(f.Simulation.BeginRound(), "begin next tooth round"); key = f.Launch(); hit = PiggyHit(f, key, d);
                Near(stage, DeviceInstanceState.Get(f.C, entity, "stage"), "stage survives retrieval and round change");
                Near(limits[stage], f.Simulation.GetOwnedDevices().Single().RemainingDurability, "new round refills current stage");
                for (int n = 0; n < limits[stage]; n++)
                {
                    f.Step(hit);
                    Near(1 + (n + 1) * bonuses[stage], f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "all stage uses including final impact award bonus");
                }
                Near(0, DeviceInstanceState.Get(f.C, entity, "stage"), "exhaustion immediately returns to hole");
                Near(0, f.Simulation.GetOwnedDevices().Single().Durability, "snapshot switches to dormant capacity");
                Check(f.Simulation.GetDeviceStatus(d.InstanceId).Contains("挖洞"), "exhaustion updates stage text");
                f.Step(hit); Near(1 + limits[stage] * bonuses[stage], f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "hole cannot keep awarding effects");
                f.Step(new MarbleContact { Key = key, Kind = MarbleContactKind.Score, TargetId = 700, Multiplier = 1, RushDisabled = true });
                Near(10 * Math.Min(f.C.Content.Definition(GameAttribute.MACHINE_MULT).MaxValue, 1 + limits[stage] * bonuses[stage]),
                    f.C.Round.Score + f.C.Round.FractionalScore, "scoring area applies accumulated factor and existing global cap");
                BagBuild(f.C); Check(f.Simulation.BeginRound(), "next round after tooth exhausted");
                Near(0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "exhausted tooth does not regrow automatically");
                BagBuild(f.C); Check(f.Simulation.TryOfferDevice(DentalFood(f.C), d.InstanceId), "hole accepts replanting");
                Near(1, DeviceInstanceState.Get(f.C, entity, "stage"), "replanting starts at steel, never old stage");
            }
            DentalOffersAndApi(); DentalPhysics(.01f); DentalPhysics(.02f); DentalOfferUi();
            Debug.Log("DENTAL_IMPLANT_VALIDATION_PASSED: " + (assertions - start) + " assertions; stages, refill, persistence, rollback, drag UI, real collisions and pass-through holes.");
        }
        private static void DentalOffersAndApi()
        {
            using (var f = new Fixture())
            {
                var d = AddDental(f); var other = AddDental(f); CampaignStateUtility.FindDevice(f.C, d.InstanceId, out var entity);
                int food = DentalFood(f.C); Check(!f.Simulation.TryOfferDevice(food, d.InstanceId), "cannot upgrade during play"); BagBuild(f.C);
                int wrong = BagFood(f.C); Check(!f.Simulation.TryOfferDevice(wrong, d.InstanceId), "different device cannot upgrade tooth");
                Check(CampaignStateUtility.FindDevice(f.C, wrong, out _), "rejected food remains in inventory");
                Check(f.Simulation.TryOfferDevice(food, d.InstanceId), "upgrade dormant tooth");
                Check(f.Simulation.BeginRound(), "start partial wear round"); var key = f.Launch(); f.Step(PiggyHit(f, key, d));
                BagBuild(f.C); Check(f.Simulation.TryOfferDevice(DentalFood(f.C), d.InstanceId), "upgrade partially worn tooth");
                Near(8, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == d.InstanceId).RemainingDurability, "upgrade refills, not merely increases cap");
                Check(f.Simulation.TryOfferDevice(DentalFood(f.C), d.InstanceId), "upgrade to maximum"); food = DentalFood(f.C);
                Check(!f.Simulation.TryOfferDevice(food, d.InstanceId), "maximum stage rejects upgrades");
                Check(CampaignStateUtility.FindDevice(f.C, food, out _), "maximum stage rejection preserves food");
                CampaignStateUtility.FindDevice(f.C, other.InstanceId, out var second);
                Near(0, DeviceInstanceState.Get(f.C, second, "stage"), "stages are isolated per instance");
                var api = new DeviceEventContext(f.C, entity);
                Near(0, api.GetInstanceValue("absent"), "missing persistent number defaults to zero");
                api.SetInstanceValue("custom", 2.5); Near(2.5, api.GetInstanceValue("custom"), "persistent state supports finite fractions");
                ExpectArgument(() => api.SetInstanceValue("bad", double.NaN)); ExpectArgument(() => api.GetInstanceValue(""));
                ExpectArgument(() => api.SetInstanceValue(new string('x', 121), 1));
                ExpectArgument(() => api.SetDurabilityLimit(-2)); ExpectArgument(() => api.SetDurabilityLimit(10001));
                api.SetDurabilityLimit(-1); Near(-1, api.RemainingDurability, "runtime capacity supports infinity");
                api.SetDurabilityLimit(5); api.RestoreDurability(); Near(5, api.RemainingDurability, "restore resets spent uses");
                Check(DeviceDurabilitySystem.TryUse(f.C, entity), "generic wear operation succeeds");
                api.SetDurabilityLimit(8); Near(7, api.RemainingDurability, "changing capacity preserves already spent uses");
                api.RestoreDurability(); Near(8, api.RemainingDurability, "refill is a separate operation");
                api.Close(); ExpectExpired(() => api.GetInstanceValue("custom")); ExpectExpired(() => api.SetDurabilityLimit(2)); ExpectExpired(() => api.RestoreDurability());
                var contact = CashApi(f, key, d); contact.SetInstanceValue("custom", -4);
                Near(-4, DeviceInstanceState.Get(f.C, entity, "custom"), "contact and event share persistent state");
                contact.SetDurabilityLimit(3); Near(3, contact.DurabilityLimit, "contact supports runtime capacity");
                contact.Close(); ExpectExpired(() => contact.SetInstanceValue("custom", 0)); ExpectExpired(() => contact.RestoreDurability());
                // The offer's values, wear and capacity must be invisible until accepted commit.
                CampaignStateUtility.FindDevice(f.C, food, out var source);
                var offer = new DeviceOfferContext(f.C, entity, source);
                Near(9028, offer.DefinitionId, "offer exposes receiver definition for generic same-type matching");
                offer.SetInstanceValue("custom", 100); offer.SetDurabilityLimit(9); offer.RestoreDurability();
                Near(-4, DeviceInstanceState.Get(f.C, entity, "custom"), "offer state remains staged");
                Near(3, DeviceDurabilitySystem.Limit(f.C, f.C.Manager.GetComponentData<OwnedDeviceData>(entity)), "offer capacity remains staged");
                offer.Close(); ExpectExpired(() => offer.GetInstanceValue("custom")); ExpectExpired(() => offer.SetDurabilityLimit(1));
                Check(f.Simulation.BeginRound(), "advance generic persistent state round");
                Near(-4, DeviceInstanceState.Get(f.C, entity, "custom"), "generic persistent state survives round boundary");
                BagBuild(f.C); Check(f.Simulation.SellDevice(d.InstanceId), "grown device can be sold");
                Check(!f.C.Manager.Exists(entity), "persistent state lifetime ends with inventory entity");
                food = DentalFood(f.C); CampaignStateUtility.FindDevice(f.C, food, out var fresh);
                Near(0, DeviceInstanceState.Get(f.C, fresh, "stage"), "new purchase does not inherit sold stage");
            }
        }
    }
}
