using System;
using System.Linq;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void MetadataAndDurability()
        {
            int start = assertions;
            foreach (var definition in DeviceResourceCatalog.Current.Definitions())
            {
                var metadata = DeviceResourceCatalog.Current.Get(definition.Id).Package.Metadata;
                Check(metadata.rarity == "common" && definition.Rarity == DeviceRarity.Common, "existing packages default to common rarity");
                Near(20, metadata.durability, "package default durability"); Near(20, definition.Durability, "catalog durability authority");
                Near(5, definition.Price, "catalog buys for purchasePrice"); Near(metadata.value, definition.SellPrice, "catalog sells for value");
            }
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 113);
                var device = f.Add(definition);
                var second = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9000));
                for (int i = 0; i < 21; i++)
                {
                    var key = f.Launch(); double before = f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier;
                    f.Step(f.Hit(key, device));
                    Near(i < 20 ? before * definition.ScoreMultiplier : before, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "twentieth use applies; twenty-first is rejected");
                    Near(Math.Max(0, 19 - i), f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == device.InstanceId).RemainingDurability, "per-instance count never goes negative");
                }
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == second.InstanceId).RemainingDurability, "other device's durability is independent");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Check(f.Simulation.BeginRound(), "start next round with owned devices");
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == device.InstanceId).RemainingDurability, "next round restores uses without recreating ownership");
            }
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 113); definition.Durability = -1;
                var device = f.Add(definition);
                for (int i = 0; i < 25; i++)
                {
                    var key = f.Launch(); double before = f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier;
                    f.Step(f.Hit(key, device));
                    Near(before * definition.ScoreMultiplier, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "unlimited device remains effective past twenty uses");
                }
                var snapshot = f.Simulation.GetOwnedDevices().Single();
                Check(snapshot.RemainingDurability == -1 && !snapshot.Exhausted, "minus one represents infinite remaining uses");
                Check(Presentation.DeviceDisplayText.Durability(snapshot) == "无限耐久", "unlimited display is explicit");
            }
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 113);
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Shop; f.C.Round = round;
                var campaign = f.C.Campaign; campaign.Coins = 1000; f.C.Campaign = campaign;
                var offers = f.C.Manager.GetBuffer<ShopOfferData>(f.C.RoundEntity); offers.Clear();
                offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = definition.Id, Price = 999 });
                Near(5, f.Simulation.GetShopOffers()[0].Price, "display replaces stale price with metadata purchase price");
                Check(f.Simulation.BuyOffer(0), "real shop buys the device"); Near(995, f.C.Campaign.Coins, "real purchase deducts five coins");
                var owned = f.Simulation.GetOwnedDevices().Single();
                Near(definition.SellPrice, owned.SellPrice, "inventory displays metadata sale price");
                f.C.Balance.Campaign.SellRatio = 0;
                Check(f.Simulation.SellDevice(owned.InstanceId), "real inventory can sell the device");
                Near(995 + definition.SellPrice, f.C.Campaign.Coins, "sale credits value independently of original paid price and old sale ratio");
            }
            DeferredDurabilityApi();
            Debug.Log("METADATA_DURABILITY_VALIDATION_PASSED: " + (assertions - start) + " assertions; metadata authority, exact wear, deferred batch uses, unlimited use and real purchase/sale.");
        }

        private static void DeferredDurabilityApi()
        {
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 113); definition.Durability = 1;
                var owned = f.Add(definition);
                CampaignStateUtility.FindDevice(f.C, owned.InstanceId, out var device);
                var first = new DeviceEventContext(f.C, device);
                Check(!first.TryUseOnce(null) && !first.TryUseOnce("") && !first.TryUseOnce(new string('界', 41)), "invalid batch keys never spend durability");
                Check(first.CanUseOnce("batch:1") && first.RemainingDurability == 1, "batch preflight costs nothing");
                Check(first.TryUseOnce("batch:1") && first.RemainingDurability == 0, "first successful batch member spends one use");
                first.Close();
                bool closed = false; try { first.TryUseOnce("batch:1"); } catch (InvalidOperationException) { closed = true; }
                Check(closed, "deferred use rejects an expired context");
                var next = new DeviceEventContext(f.C, device);
                Check(next.CanUseOnce("batch:1") && next.TryUseOnce("batch:1"), "another callback can finish the same paid batch after exhaustion");
                Check(!next.CanUseOnce("batch:2") && !next.TryUseOnce("batch:2"), "a new batch cannot reuse the final durability");
                Check(!next.TryKeepActive(0) && !next.TryKeepActive(float.NaN) && !next.TryKeepActive(61), "invalid continuation durations rejected");
                Check(next.TryKeepActive(.5f) && DeviceDurabilitySystem.CanRun(f.C, device), "last effect may finish during a bounded continuation");
                var other = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9000));
                CampaignStateUtility.FindDevice(f.C, other.InstanceId, out var otherEntity);
                var otherApi = new DeviceEventContext(f.C, otherEntity);
                Check(otherApi.TryUseOnce("batch:1") && otherApi.RemainingDurability == 19, "batch keys are isolated per device"); otherApi.Close();
                var round = f.C.Round; round.Time += .6; f.C.Round = round;
                Check(!DeviceDurabilitySystem.CanRun(f.C, device), "bounded continuation expires without a callback"); next.Close();
                round.Phase = (byte)RoundPhase.Build; f.C.Round = round; f.Simulation.BeginRound();
                next = new DeviceEventContext(f.C, device);
                Check(next.TryUseOnce("batch:1") && next.RemainingDurability == 0, "same batch name charges again in a new round");
                Check(!DeviceDurabilitySystem.CanRun(f.C, device), "previous round's continuation never carries over"); next.Close();
                definition.Durability = -1;
                next = new DeviceEventContext(f.C, device);
                for (int i = 0; i < 25; i++) Check(next.TryUseOnce("unlimited:" + i), "unlimited batches succeed beyond twenty");
                Check(next.RemainingDurability == -1, "unlimited batch registration preserves minus one"); next.Close();
            }
        }
    }
}
