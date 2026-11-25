using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    /// <summary>Isolated campaign checks: no scene objects or live player state are changed.</summary>
    public static partial class RandomShopValidation
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
            balance.Campaign.StartingCoins = 100000;
            balance.Campaign.MaxOwnedDevices = 200;
            balance.Campaign.ShopOfferCount = 4;
            Check(balance.Devices.Length >= 4, "full shop catalog exists");
            Check(balance.Stages.Length >= 3, "the fixture can run two stages and then start the next day");
            balance.Validate();

            SamplingAndSeeds(balance);
            PurchaseAndRoundFlow(balance);
            CatalogShortages(balance);
            WeightedRarities(balance);
            Debug.Log("RANDOM_SHOP_VALIDATION_PASSED: " + assertions + " assertions; " +
                balance.Devices.Length + " devices reachable; 4 device offers, shared run seed, exact draw counts, " +
                "purchases, sold-out protection, refresh inventory and two settlements.");
        }

        private static void SamplingAndSeeds(GameBalance balance)
        {
            var physics = new EmptyPhysics();
            using (var simulation = new MarbleSimulation(balance))
            {
                const uint seed = 240127;
                simulation.StartSession(physics, 0, seed);
                var devices = new HashSet<uint>();
                bool withoutRushDevice = false;
                int draws = Math.Max(256, balance.Devices.Length * 12);
                for (int i = 0; i < draws; i++)
                {
                    uint expected = Advance(simulation.Context.Round.RandomState, 4);
                    CampaignShopSystem.Generate(simulation.Context);
                    var offers = CheckOffers(simulation, 4);
                    foreach (var offer in offers)
                        devices.Add(offer.DefinitionId);
                    withoutRushDevice |= !offers.Any(o => o.Kind == ShopItemKind.Device &&
                        IsRush(CampaignStateUtility.Device(simulation.Context, o.DefinitionId)));
                    Check(simulation.Context.Round.RandomState == expected,
                        "shop consumes exactly four draws from the shared stream");
                }
                Check(devices.SetEquals(balance.Devices.Select(d => d.Id)), "every device is reachable from the full catalog");
                if (balance.Devices.Count(d => !IsRush(d)) >= 4)
                    Check(withoutRushDevice, "a Rush device is not forced into every shop");

                var first = DrawSequence(simulation, physics, seed, 8);
                var replay = DrawSequence(simulation, physics, seed, 8);
                Check(first.SequenceEqual(replay), "one run seed reproduces the complete shop sequence");
                Check(!first.SequenceEqual(DrawSequence(simulation, physics, seed + 1, 8)), "different seeds produce different sequences");

                var reordered = balance.Copy();
                Array.Reverse(reordered.Devices);
                using (var other = new MarbleSimulation(reordered))
                    Check(first.SequenceEqual(DrawSequence(other, physics, seed, 8)),
                        "catalog enumeration order does not alter seeded offers");

                simulation.StartSession(physics);
                Check(simulation.Context.Round.RandomState == balance.Marble.RandomSeed,
                    "a simulation without an override starts from the one configured seed");
                CampaignShopSystem.Generate(simulation.Context);
                simulation.StartSession(physics);
                Check(simulation.Context.Round.RandomState == balance.Marble.RandomSeed,
                    "new run resets the shared stream instead of retaining the last shop state");
            }
        }

        private static string[] DrawSequence(MarbleSimulation simulation, IMarblePhysics physics, uint seed, int count)
        {
            simulation.StartSession(physics, 0, seed);
            Check(simulation.Context.Round.RandomState == seed, "explicit run seed initializes the shared RNG");
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
            balance = balance.Copy();
            balance.Campaign.StartingCoins = 1000;
            string sequence = $@"{{""schemaVersion"":1,""sequenceId"":""shop-validation"",""days"":[
                {{""dayId"":""first"",""order"":1,""productionPlanId"":""first"",""productionFeatures"":[""Shop""]}},
                {{""dayId"":""second"",""order"":2,""productionPlanId"":""second"",""productionFeatures"":[""Shop""]}}],
                ""productionPlans"":[
                {{""planId"":""first"",""firstStageId"":{balance.Stages[0].Id},""lastStageId"":{balance.Stages[1].Id}}},
                {{""planId"":""second"",""firstStageId"":{balance.Stages[2].Id},""lastStageId"":{balance.Stages[2].Id}}}]}}";
            using (var simulation = new MarbleSimulation(balance))
            {
                var definition = GameDaysLoader.Parse(sequence, balance);
                Check(simulation.TryStartDayCampaign(definition, new EmptyPhysics(), out string error, 77031), error);
                long morning = simulation.Day.Version;
                Check(simulation.Day.Phase == DayPhase.WakeUp && simulation.GetShopOffers().Length == 0,
                    "a new morning has no stock until the shop is visited");
                Check(!simulation.BuyOffer(0) && !simulation.RefreshShop(), "purchase and refresh require an open shop");
                Check(simulation.TryOpenShop(morning), "first Morning Prep visit opens the shop");
                Check(simulation.Day.RandomState == Advance(77031, 4), "first opening consumes four draws from the run RNG");

                var offers = CheckOffers(simulation, 4);
                BuyAndCheck(simulation, offers[0]);
                BuyAndCheck(simulation, offers[1]);
                int[] owned = simulation.GetOwnedDevices().Select(d => d.InstanceId).OrderBy(id => id).ToArray();
                long coins = simulation.Session.Coins;
                long refreshCost = CampaignShopSystem.Price(simulation.Context, balance.Campaign.ShopRefreshCost);
                uint expected = Advance(simulation.Context.Round.RandomState, 4);
                int refreshCount = simulation.Session.ShopRefreshCount;
                Check(simulation.RefreshShop(), "paid shop refresh succeeds");
                Check(simulation.Session.Coins == coins - refreshCost &&
                    simulation.Session.ShopRefreshCount == refreshCount + 1, "refresh charges once and increments its counter");
                Check(simulation.Context.Round.RandomState == expected, "paid refresh advances the shared RNG four times");
                Check(owned.SequenceEqual(simulation.GetOwnedDevices().Select(d => d.InstanceId).OrderBy(id => id)),
                    "refresh preserves purchased devices");
                Check(CheckOffers(simulation, 4).All(o => !o.Sold), "refresh replaces sold offers with fresh stock");

                BuyAndCheck(simulation, simulation.GetShopOffers()[0]);
                owned = simulation.GetOwnedDevices().Select(d => d.InstanceId).OrderBy(id => id).ToArray();
                Check(simulation.TryCloseShop(morning) && simulation.Day.Phase == DayPhase.WakeUp,
                    "closing the shop returns to the same morning");
                uint beforeReopen = simulation.Day.RandomState;
                Check(simulation.TryOpenShop(morning) && simulation.GetShopOffers()[0].Sold && simulation.Day.RandomState == beforeReopen,
                    "reopening retains sold stock and uses no draws");
                Check(simulation.TryCloseShop(morning) && simulation.TryCompleteWakeUp(morning, out error), "production starts after closing the shop");
                Check(!simulation.BuyOffer(0) && !simulation.RefreshShop() && !simulation.TryOpenShop(simulation.Day.Version),
                    "production cannot access the shop or reroll stock");
                CompleteRound(simulation, RoundPhase.Build);
                Check(simulation.Session.StageIndex == 1, "the first settlement advances straight to the next build");
                CompleteRound(simulation, RoundPhase.Won);
                Check(simulation.TryCompleteDaySettlement(simulation.Day.Version, out error), error);
                Check(simulation.TryCompleteRest(simulation.Day.Version, out error), error);
                Check(simulation.GetShopOffers().Length == 0, "the next day starts with ungenerated stock");
                uint secondMorning = simulation.Day.RandomState;
                Check(simulation.TryOpenShop(simulation.Day.Version) && CheckOffers(simulation, 4).All(o => !o.Sold),
                    "the next morning generates new stock on its first visit");
                Check(simulation.Day.RandomState == Advance(secondMorning, 4), "new stock continues the run RNG");
                Check(owned.SequenceEqual(simulation.GetOwnedDevices().Select(d => d.InstanceId).OrderBy(id => id)),
                    "moving through stages and days preserves purchased inventory");
            }
        }

        private static void CompleteRound(MarbleSimulation simulation, RoundPhase nextPhase)
        {
            uint before = simulation.Context.Round.RandomState;
            Check(simulation.BeginRound(), "placement starts the round");
            Check(simulation.Context.Round.RandomState == before, "next round continues the shared stream without rewinding");
            // Supply only the score required to pass; the real CashOut/settlement code handles phase changes.
            var round = simulation.Context.Round;
            round.Score = Math.Max(0, round.TargetScore - simulation.Context.Campaign.TotalScore);
            simulation.Context.Round = round;
            Check(simulation.CashOut(), "eligible round cashes out");
            Check(simulation.Session.Phase == nextPhase, "settlement advances production");
            Check(simulation.Context.Round.RandomState == before, "settlement without a gamble consumes no random draws");
        }

        private static void BuyAndCheck(MarbleSimulation simulation, ShopOfferSnapshot offer)
        {
            uint before = simulation.Context.Round.RandomState;
            long coins = simulation.Session.Coins;
            int devices = simulation.GetOwnedDevices().Length;
            Check(simulation.BuyOffer(offer.Index), "available offer can be purchased");
            Check(simulation.Session.Coins == coins - offer.Price, "purchase deducts its displayed price once");
            Check(offer.Kind == ShopItemKind.Device, "purchased offer is a device");
            {
                int quantity = CampaignStateUtility.Device(simulation.Context, offer.DefinitionId).PurchaseQuantity;
                Check(simulation.GetOwnedDevices().Length == devices + quantity, "device purchase adds its entire pack");
                Check(simulation.GetOwnedDevices().Count(d => d.DefinitionId == offer.DefinitionId) >= quantity,
                    "purchased device definition enters inventory");
            }
            Check(simulation.GetShopOffers()[offer.Index].Sold, "purchased offer is marked sold");
            coins = simulation.Session.Coins;
            devices = simulation.GetOwnedDevices().Length;
            Check(!simulation.BuyOffer(offer.Index), "sold offer cannot be purchased again");
            Check(simulation.Session.Coins == coins && simulation.GetOwnedDevices().Length == devices,
                "rejected repeat purchase changes no wallet or inventory");
            Check(simulation.Context.Round.RandomState == before, "purchase and rejected repeat purchase consume no randomness");
        }

        private static void CatalogShortages(GameBalance original)
        {
            foreach (int available in new[] { 1, 2 })
            {
                var balance = original.Copy();
                balance.Devices = balance.Devices.Take(available).ToArray();
                using (var simulation = new MarbleSimulation(balance))
                {
                    simulation.StartSession(new EmptyPhysics(), 0, 91);
                    CampaignShopSystem.Generate(simulation.Context);
                    CheckOffers(simulation, available);
                    Check(simulation.Context.Round.RandomState == Advance(91, available),
                        "catalog shortage consumes only actual offers, not missing slots");
                }
            }
        }

        private static ShopOfferSnapshot[] CheckOffers(MarbleSimulation simulation, int deviceCount)
        {
            var offers = simulation.GetShopOffers();
            Check(offers.Length == deviceCount && offers.All(o => o.Kind == ShopItemKind.Device),
                "shop contains only available device offers");
            Check(offers.Select(o => o.DefinitionId).Distinct().Count() == offers.Length,
                "shop has no duplicate item definitions");
            Check(offers.All(o => CampaignStateUtility.Device(simulation.Context, o.DefinitionId) != null),
                "all offers belong to the active catalog");
            return offers;
        }

        private static bool IsRush(DeviceDefinition definition) => definition.RushCategory || definition.RushChanceAdd > 0;

        private static uint Advance(uint state, int draws)
        {
            for (int i = 0; i < draws; i++) MarbleRules.NextRandom(ref state);
            return state;
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception("Random shop validation: " + message);
        }

        private sealed class EmptyPhysics : IMarbleDayPhysics, IMarbleDayPhysicsCheckpoint
        {
            public bool TrySpawn(MarbleSpawnData data) => false;
            public bool TryGetPosition(MarbleKey key, out Vector3 position)
            { position = Vector3.zero; return false; }
            public void Remove(MarbleKey key) { }
            public void ResetDayPhysics() { }
            public IMarbleDayPhysicsCheckpoint CaptureDayCheckpoint() => this;
            public void Restore() { }
        }
    }
}
