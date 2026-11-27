using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class RandomShopValidation
    {
        private static void WeightedRarities(GameBalance original)
        {
            int before = assertions;
            var uncommon = new HashSet<uint> { 103, 9003, 9007, 9008, 9009, 9011, 9012, 9014, 9016, 9017, 9019, 9021, 9022, 9026, 9028, 9029, 9030, 9032, 9035, 9036 };
            var rare = new HashSet<uint> { 9005, 9006, 9018, 9027, 9031, 9037 };
            foreach (var definition in original.Devices)
            {
                var expected = uncommon.Contains(definition.Id) ? DeviceRarity.Uncommon : rare.Contains(definition.Id) ? DeviceRarity.Rare : DeviceRarity.Common;
                Check(definition.Rarity == expected, "catalog matches the requested device tiers: " + definition.Name);
                var metadata = DeviceResourceCatalog.Current.Get(definition.Id).Metadata;
                Check(metadata.rarity == expected.ToString().ToLowerInvariant(), "shop tier follows metadata.json");
            }
            Check(Presentation.DeviceDisplayText.Rarity(DeviceRarity.Uncommon) == "罕见" &&
                Presentation.DeviceDisplayText.Rarity(DeviceRarity.Rare) == "稀有", "UI uses the new four-tier vocabulary");
            Check(DeviceRarityRules.ShopWeight(DeviceRarity.Legendary) == 3,
                "legendary weight is ready for future content");

            SampleRarities(original, "current catalog without legendary", new[] { 60d / 97, 30d / 97, 7d / 97, 0d });
            // One item each in the first three tiers and many legendary items: tier weights must not
            // accidentally become per-item weights or depend on the catalog's population ratios.
            var skewed = original.Copy();
            for (int i = 0; i < skewed.Devices.Length; i++)
                skewed.Devices[i].Rarity = (DeviceRarity)Math.Min(i, 3);
            SampleRarities(skewed, "all four tiers with unequal populations", new[] { .6, .3, .07, .03 });

            var limited = original.Copy(); limited.Devices = limited.Devices.Take(4).ToArray();
            for (int i = 0; i < 4; i++) limited.Devices[i].Rarity = (DeviceRarity)i;
            using (var simulation = new MarbleSimulation(limited))
            {
                StartShopCampaign(simulation, limited, new EmptyPhysics(), 517);
                for (int i = 0; i < 32; i++)
                {
                    uint state = simulation.Context.Round.RandomState;
                    CampaignShopSystem.Generate(simulation.Context, simulation.CurrentDayDefinition.DailyLoadout);
                    var offers = CheckOffers(simulation, 4);
                    Check(offers.Select(o => o.Rarity).Distinct().Count() == 4, "exhausted tiers are skipped and every remaining item fills a slot");
                    Check(simulation.Context.Round.RandomState == Advance(state, 4), "exhausted tiers consume no retry draws");
                }
            }
            limited.Devices = limited.Devices.Take(1).ToArray(); limited.Devices[0].Rarity = DeviceRarity.Legendary;
            using (var simulation = new MarbleSimulation(limited))
            {
                StartShopCampaign(simulation, limited, new EmptyPhysics(), 71); CampaignShopSystem.Generate(simulation.Context, simulation.CurrentDayDefinition.DailyLoadout);
                Check(CheckOffers(simulation, 1)[0].Rarity == DeviceRarity.Legendary, "a lone legendary fills the only available slot");
                Check(simulation.Context.Round.RandomState == Advance(71, 1), "catalog shortage does not consume missing-slot draws");
            }
            Debug.Log("RARITY_SHOP_VALIDATION_PASSED: " + (assertions - before) + " assertions; configured tiers, weighted draws, equal per-tier items, shortages and shared RNG.");
        }

        private static void SampleRarities(GameBalance source, string label, double[] expected)
        {
            var balance = source.Copy();
            const int samples = 40000;
            var counts = new int[4]; var hits = balance.Devices.ToDictionary(d => d.Id, d => 0);
            using (var simulation = new MarbleSimulation(balance))
            {
                StartShopCampaign(simulation, balance, new EmptyPhysics(), 19357, offers: 1);
                for (int i = 0; i < samples; i++)
                {
                    CampaignShopSystem.Generate(simulation.Context, simulation.CurrentDayDefinition.DailyLoadout);
                    var offer = simulation.Context.Manager.GetBuffer<ShopOfferData>(simulation.Context.RoundEntity)[0];
                    counts[(int)CampaignStateUtility.Device(simulation.Context, offer.DefinitionId).Rarity]++; hits[offer.DefinitionId]++;
                }
                Check(simulation.Context.Round.RandomState == Advance(19357, samples), "weighted samples use one shared draw per actual offer");
            }
            for (int tier = 0; tier < 4; tier++)
                Check(Math.Abs(counts[tier] / (double)samples - expected[tier]) < .008,
                    label + " frequency for " + (DeviceRarity)tier + ": " + counts[tier] + "/" + samples);
            // Pearson statistic checks equality within each tier without requiring arbitrary exact counts.
            foreach (var group in balance.Devices.GroupBy(d => d.Rarity))
            {
                double mean = counts[(int)group.Key] / (double)group.Count();
                double statistic = group.Sum(d => (hits[d.Id] - mean) * (hits[d.Id] - mean) / mean);
                Check(group.All(d => hits[d.Id] > 0) && statistic < group.Count() * 3 + 20, label + " samples equally within " + group.Key);
            }
            Debug.Log("RARITY_SAMPLE: " + label + "; common/uncommon/rare/legendary = " + string.Join("/", counts) + " of " + samples + ".");
        }
    }
}
