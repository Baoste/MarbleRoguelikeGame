using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    /// <summary>Isolated campaign checks: no scene objects or live player state are changed.</summary>
    public static class RandomShopValidation
    {
        private static int assertions;

        [MenuItem("Marbles ECS/Validate Random Shop")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Run random shop validation in Play Mode.");

            assertions = 0;
            var balance = GameBalanceLoader.Load();
            foreach (var device in balance.Devices) device.StartingCount = 0;
            foreach (var drug in balance.Drugs) drug.StartingCount = 0;
            balance.Campaign.StartingCoins = 100000;
            balance.Campaign.MaxOwnedDevices = 200;
            balance.Campaign.MaxDrugInventory = 1000;
            balance.Campaign.ShopOfferCount = 4;
            balance.Campaign.ShopDrugOfferCount = 1;
            Check(balance.Devices.Length >= 3 && balance.Drugs.Length >= 1, "full shop catalog exists");
            Check(balance.Stages.Length >= 3, "two non-final settlement rounds are available");
            balance.Validate();

            SamplingAndSeeds(balance);
            PurchaseAndRoundFlow(balance);
            CategoryShortages(balance);
            Debug.Log("RANDOM_SHOP_VALIDATION_PASSED: " + assertions + " assertions; " +
                balance.Devices.Length + " devices / " + balance.Drugs.Length +
                " drugs reachable; 1 drug + 3 devices, seeds, gameplay RNG isolation, " +
                "purchases, sold-out protection, refresh inventory and two settlements.");
        }

        private static void SamplingAndSeeds(GameBalance balance)
        {
            var physics = new EmptyPhysics();
            using (var simulation = new MarbleSimulation(balance))
            {
                const uint seed = 240127;
                simulation.StartSession(physics, 0, 0, seed);
                var devices = new HashSet<uint>();
                var drugs = new HashSet<uint>();
                bool withoutRushDevice = false, withoutRushDrug = false;
                uint gameplayState = simulation.Context.Campaign.RandomState;
                uint roundState = simulation.Context.Round.RandomState;
                int draws = Math.Max(256, Math.Max(balance.Devices.Length, balance.Drugs.Length) * 12);
                for (int i = 0; i < draws; i++)
                {
                    uint previousShopState = simulation.Context.Campaign.ShopRandomState;
                    CampaignShopSystem.Generate(simulation.Context);
                    var offers = CheckOffers(simulation, 3, 1);
                    foreach (var offer in offers)
                    {
                        if (offer.Kind == ShopItemKind.Device) devices.Add(offer.DefinitionId);
                        else drugs.Add(offer.DefinitionId);
                    }
                    withoutRushDevice |= !offers.Any(o => o.Kind == ShopItemKind.Device &&
                        IsRush(CampaignStateUtility.Device(simulation.Context, o.DefinitionId)));
                    withoutRushDrug |= !offers.Any(o => o.Kind == ShopItemKind.Drug &&
                        IsRush(CampaignStateUtility.Drug(simulation.Context, o.DefinitionId)));
                    Check(simulation.Context.Campaign.ShopRandomState != previousShopState, "each generation advances shop RNG");
                    Check(simulation.Context.Campaign.RandomState == gameplayState &&
                        simulation.Context.Round.RandomState == roundState, "shop leaves both gameplay RNG states unchanged");
                }
                Check(devices.SetEquals(balance.Devices.Select(d => d.Id)), "every device is reachable from the full catalog");
                Check(drugs.SetEquals(balance.Drugs.Select(d => d.Id)), "every drug is reachable from the full catalog");
                if (balance.Devices.Count(d => !IsRush(d)) >= 3)
                    Check(withoutRushDevice, "a Rush device is not forced into every shop");
                if (balance.Drugs.Any(d => !IsRush(d)))
                    Check(withoutRushDrug, "a Rush drug is not forced into every shop");

                var first = DrawSequence(simulation, physics, seed, 8);
                var replay = DrawSequence(simulation, physics, seed, 8);
                Check(first.SequenceEqual(replay), "an explicit seed reproduces the complete shop sequence");
                Check(!first.SequenceEqual(DrawSequence(simulation, physics, seed + 1, 8)), "different seeds produce different sequences");

                var freshSeeds = new HashSet<uint>();
                for (int i = 0; i < 4; i++)
                {
                    simulation.StartSession(physics);
                    uint fresh = simulation.Context.Campaign.ShopRandomState;
                    Check(fresh != 0, "default new-session shop seed is nonzero");
                    freshSeeds.Add(fresh);
                }
                Check(freshSeeds.Count > 1, "normal new sessions do not reuse a fixed shop sequence");
            }
        }

        private static string[] DrawSequence(MarbleSimulation simulation, IMarblePhysics physics, uint seed, int count)
        {
            simulation.StartSession(physics, 0, 0, seed);
            Check(simulation.Context.Campaign.ShopRandomState == seed, "explicit seed initializes shop RNG");
            var sequence = new string[count];
            for (int i = 0; i < count; i++)
            {
                CampaignShopSystem.Generate(simulation.Context);
                sequence[i] = string.Join(",", simulation.GetShopOffers().Select(o => (int)o.Kind + ":" + o.DefinitionId));
            }
            return sequence;
        }

        private static void PurchaseAndRoundFlow(GameBalance balance)
        {
            using (var simulation = new MarbleSimulation(balance))
            {
                simulation.StartSession(new EmptyPhysics(), 0, 0, 77031);
                Check(simulation.Session.Phase == RoundPhase.Build && simulation.GetShopOffers().Length == 0,
                    "new run starts in Build without stale shop offers");
                Check(!simulation.BuyOffer(0) && !simulation.RefreshShop(), "shop commands are unavailable before settlement");
                CompleteRound(simulation);
                Check(simulation.Session.StageIndex == 1, "first settlement advances one stage");

                var offers = CheckOffers(simulation, 3, 1);
                BuyAndCheck(simulation, offers.First(o => o.Kind == ShopItemKind.Drug));
                BuyAndCheck(simulation, offers.First(o => o.Kind == ShopItemKind.Device));
                int[] owned = simulation.GetOwnedDevices().Select(d => d.InstanceId).OrderBy(id => id).ToArray();
                string inventory = DrugCounts(simulation);
                long coins = simulation.Session.Coins;
                long refreshCost = CampaignShopSystem.Price(simulation.Context, balance.Campaign.ShopRefreshCost);
                uint shopState = simulation.Context.Campaign.ShopRandomState;
                uint gameplayState = simulation.Context.Campaign.RandomState;
                int refreshCount = simulation.Session.ShopRefreshCount;
                Check(simulation.RefreshShop(), "paid shop refresh succeeds");
                Check(simulation.Session.Coins == coins - refreshCost &&
                    simulation.Session.ShopRefreshCount == refreshCount + 1, "refresh charges once and increments its counter");
                Check(simulation.Context.Campaign.ShopRandomState != shopState &&
                    simulation.Context.Campaign.RandomState == gameplayState, "refresh advances only shop RNG");
                Check(owned.SequenceEqual(simulation.GetOwnedDevices().Select(d => d.InstanceId).OrderBy(id => id)) &&
                    inventory == DrugCounts(simulation), "refresh preserves purchased devices and drugs");
                Check(CheckOffers(simulation, 3, 1).All(o => !o.Sold), "refresh replaces sold offers with fresh stock");

                BuyAndCheck(simulation, simulation.GetShopOffers().First(o => o.Kind == ShopItemKind.Drug));
                inventory = DrugCounts(simulation);
                Check(simulation.EnterBuild(), "shop proceeds to next placement round");
                Check(!simulation.BuyOffer(0) && !simulation.RefreshShop(), "placement cannot purchase or reroll hidden shop offers");
                CompleteRound(simulation);
                Check(simulation.Session.StageIndex == 2, "second settlement advances one more stage");
                Check(CheckOffers(simulation, 3, 1).All(o => !o.Sold), "second settlement automatically replaces sold stock");
                Check(owned.SequenceEqual(simulation.GetOwnedDevices().Select(d => d.InstanceId).OrderBy(id => id)) &&
                    inventory == DrugCounts(simulation), "next-round settlement preserves purchased inventory");
            }
        }

        private static void CompleteRound(MarbleSimulation simulation)
        {
            Check(simulation.BeginRound(), "placement starts the round");
            // Supply only the score required to pass; the real CashOut/settlement code handles phase changes.
            var round = simulation.Context.Round;
            round.Score = Math.Max(0, round.TargetScore - simulation.Context.Campaign.TotalScore);
            simulation.Context.Round = round;
            uint shopState = simulation.Context.Campaign.ShopRandomState;
            uint gameplayState = round.RandomState;
            Check(simulation.CashOut(), "eligible round cashes out");
            Check(simulation.Session.Phase == RoundPhase.Shop, "settlement opens the shop");
            Check(simulation.Context.Campaign.ShopRandomState != shopState, "settlement automatically generates a new shop");
            Check(simulation.Context.Campaign.RandomState == gameplayState,
                "a settlement without a pending gamble preserves gameplay RNG");
            CheckOffers(simulation, 3, 1);
        }

        private static void BuyAndCheck(MarbleSimulation simulation, ShopOfferSnapshot offer)
        {
            long coins = simulation.Session.Coins;
            int devices = simulation.GetOwnedDevices().Length;
            int drugs = simulation.GetDrugInventory().Sum(d => d.Count);
            Check(simulation.BuyOffer(offer.Index), "available offer can be purchased");
            Check(simulation.Session.Coins == coins - offer.Price, "purchase deducts its displayed price once");
            if (offer.Kind == ShopItemKind.Device)
            {
                int quantity = CampaignStateUtility.Device(simulation.Context, offer.DefinitionId).PurchaseQuantity;
                Check(simulation.GetOwnedDevices().Length == devices + quantity, "device purchase adds its entire pack");
                Check(simulation.GetOwnedDevices().Count(d => d.DefinitionId == offer.DefinitionId) >= quantity,
                    "purchased device definition enters inventory");
            }
            else Check(simulation.GetDrugInventory().Sum(d => d.Count) == drugs + 1,
                "drug purchase adds one dose to inventory");
            Check(simulation.GetShopOffers()[offer.Index].Sold, "purchased offer is marked sold");
            coins = simulation.Session.Coins;
            devices = simulation.GetOwnedDevices().Length;
            drugs = simulation.GetDrugInventory().Sum(d => d.Count);
            Check(!simulation.BuyOffer(offer.Index), "sold offer cannot be purchased again");
            Check(simulation.Session.Coins == coins && simulation.GetOwnedDevices().Length == devices &&
                simulation.GetDrugInventory().Sum(d => d.Count) == drugs, "rejected repeat purchase changes no wallet or inventory");
        }

        private static void CategoryShortages(GameBalance original)
        {
            var balance = original.Copy();
            balance.Devices = new[] { balance.Devices[0] };
            using (var simulation = new MarbleSimulation(balance))
            {
                simulation.StartSession(new EmptyPhysics(), 0, 0, 91);
                CampaignShopSystem.Generate(simulation.Context);
                CheckOffers(simulation, 1, 1);
            }
            balance = original.Copy();
            balance.Campaign.ShopDrugOfferCount = 3;
            balance.Drugs = new[] { balance.Drugs[0] };
            using (var simulation = new MarbleSimulation(balance))
            {
                simulation.StartSession(new EmptyPhysics(), 0, 0, 92);
                CampaignShopSystem.Generate(simulation.Context);
                CheckOffers(simulation, 1, 1);
            }
        }

        private static ShopOfferSnapshot[] CheckOffers(MarbleSimulation simulation, int deviceCount, int drugCount)
        {
            var offers = simulation.GetShopOffers();
            Check(offers.Length == deviceCount + drugCount &&
                offers.Count(o => o.Kind == ShopItemKind.Device) == deviceCount &&
                offers.Count(o => o.Kind == ShopItemKind.Drug) == drugCount, "shop respects category quotas without cross-category backfill");
            Check(offers.Select(o => ((ulong)o.Kind << 32) | o.DefinitionId).Distinct().Count() == offers.Length,
                "shop has no duplicate item definitions");
            Check(offers.All(o => o.Kind == ShopItemKind.Device
                ? CampaignStateUtility.Device(simulation.Context, o.DefinitionId) != null
                : CampaignStateUtility.Drug(simulation.Context, o.DefinitionId) != null), "all offers belong to the active catalog");
            return offers;
        }

        private static string DrugCounts(MarbleSimulation simulation) => string.Join(",",
            simulation.GetDrugInventory().OrderBy(d => d.DefinitionId).Select(d => d.DefinitionId + ":" + d.Count));

        private static bool IsRush(DeviceDefinition definition) => definition.RushCategory || definition.RushChanceAdd > 0;
        private static bool IsRush(DrugDefinition definition) => definition.Kind == DrugKind.Adrenaline ||
            definition.Kind == DrugKind.RushFruit || definition.Kind == DrugKind.RushDuration;

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception("Random shop validation: " + message);
        }

        private sealed class EmptyPhysics : IMarblePhysics
        {
            public bool TrySpawn(MarbleSpawnData data) => false;
            public bool TryGetPosition(MarbleKey key, out Vector3 position)
            { position = Vector3.zero; return false; }
            public void Remove(MarbleKey key) { }
        }
    }
}
