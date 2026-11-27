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
            var defaults = DeviceMetadata.Read("{\"name\":\"Test\",\"description\":\"Test\",\"type\":\"device\"}", "fixture");
            Near(20, defaults.durability, "missing metadata durability defaults to 20");
            var legacy = new DeviceDefinition { BiomassScrapPrice = defaults.biomassScrapPrice, Rarity = DeviceRarity.Rare };
            Near(23, legacy.InstallPrice, "an older package without a scrap price uses its tier default");
            foreach (var definition in DeviceResourceCatalog.Current.Definitions())
            {
                var metadata = DeviceResourceCatalog.Current.Get(definition.Id).Metadata;
                Check(metadata.rarity == definition.Rarity.ToString().ToLowerInvariant(), "catalog rarity follows metadata.json");
                Near(metadata.durability, definition.Durability, "catalog durability follows metadata.json");
                Check(metadata.biomassScrapPrice > 0, "current packages set an explicit scrap price");
                Near(metadata.biomassScrapPrice, definition.InstallPrice, "daily installation uses the configured scrap price");
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
            DeferredDurabilityApi();
            Debug.Log("METADATA_DURABILITY_VALIDATION_PASSED: " + (assertions - start) + " assertions; metadata authority, exact wear, deferred batch uses, and unlimited use.");
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
