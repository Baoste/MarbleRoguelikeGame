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
            balance.Campaign.MaxOwnedDevices = 200;
            Check(balance.Devices.Length >= 4, "full shop catalog exists");
            balance.Validate();

            SamplingAndSeeds(balance);
            PurchaseAndRoundFlow(balance);
            CatalogShortages(balance);
            WeightedRarities(balance);
            Debug.Log("RANDOM_SHOP_VALIDATION_PASSED: " + assertions + " assertions; " +
                balance.Devices.Length + " devices reachable; 4 device offers, shared run seed, exact draw counts, " +
                "permission purchases, sold-out protection, refresh and stock across days.");
        }

        private static void SamplingAndSeeds(GameBalance balance)
        {
            var physics = new EmptyPhysics();
            using (var simulation = new MarbleSimulation(balance))
            {
                const uint seed = 240127;
                StartShopCampaign(simulation, balance, physics, seed);
                var devices = new HashSet<uint>();
                bool withoutRushDevice = false;
                int draws = Math.Max(256, balance.Devices.Length * 12);
                for (int i = 0; i < draws; i++)
                {
                    uint expected = Advance(simulation.Context.Round.RandomState, 4);
                    CampaignShopSystem.Generate(simulation.Context, simulation.CurrentDayDefinition.DailyLoadout);
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

                var first = DrawSequence(balance, physics, seed, 8);
                var replay = DrawSequence(balance, physics, seed, 8);
                Check(first.SequenceEqual(replay), "one run seed reproduces the complete shop sequence");
                Check(!first.SequenceEqual(DrawSequence(balance, physics, seed + 1, 8)), "different seeds produce different sequences");

                var reordered = balance.Copy();
                Array.Reverse(reordered.Devices);
                Check(first.SequenceEqual(DrawSequence(reordered, physics, seed, 8)),
                    "catalog enumeration order does not alter seeded offers");
            }

            using var fresh = new MarbleSimulation(balance);
            StartShopCampaign(fresh, balance, physics);
            Check(fresh.Day.RandomState == balance.Marble.RandomSeed, "a new run uses the configured seed without an override");
        }

        private static string[] DrawSequence(GameBalance balance, IMarblePhysics physics, uint seed, int count)
        {
            using var simulation = new MarbleSimulation(balance);
            StartShopCampaign(simulation, balance, physics, seed);
            Check(simulation.Context.Round.RandomState == seed, "explicit run seed initializes the shared RNG");
            var sequence = new string[count];
            for (int i = 0; i < count; i++)
            {
                CampaignShopSystem.Generate(simulation.Context, simulation.CurrentDayDefinition.DailyLoadout);
                sequence[i] = string.Join(",", simulation.GetShopOffers().Select(o => (int)o.Kind + ":" + o.DefinitionId));
            }
            return sequence;
        }

        private static void PurchaseAndRoundFlow(GameBalance balance)
        {
            using (var simulation = new MarbleSimulation(balance))
            {
                StartShopCampaign(simulation, balance, new EmptyPhysics(), 77031, days: 2);
                string error;
                long morning = simulation.Day.Version;
                Check(simulation.Day.Phase == DayPhase.WakeUp && simulation.GetShopOffers().Length == 0,
                    "a new morning has no stock until the shop is visited");
                Check(!simulation.BuyOffer(0) && !simulation.RefreshShop(), "purchase and refresh require an open shop");
                Check(simulation.TryOpenShop(morning), "first Morning Prep visit opens the shop");
                Check(simulation.Day.RandomState == Advance(77031, 4), "first opening consumes four draws from the run RNG");

                var offers = CheckOffers(simulation, 4);
                BuyAndCheck(simulation, offers[0]);
                BuyAndCheck(simulation, offers[1]);
                uint[] permissions = OwnedPermissions(simulation);
                long scrip = simulation.Session.CompanyScripBalance;
                long refreshCost = simulation.CurrentDayDefinition.DailyLoadout.Shop.RefreshCost.Value;
                uint expected = Advance(simulation.Context.Round.RandomState, 4);
                int refreshCount = simulation.Session.ShopRefreshCount;
                Check(simulation.RefreshShop(), "paid shop refresh succeeds");
                Check(simulation.Session.CompanyScripBalance == scrip - refreshCost &&
                    simulation.Session.ShopRefreshCount == refreshCount + 1, "refresh charges once and increments its counter");
                Check(simulation.Context.Round.RandomState == expected, "paid refresh advances the shared RNG four times");
                Check(permissions.SequenceEqual(OwnedPermissions(simulation)), "refresh preserves purchased permissions");
                Check(CheckOffers(simulation, 4).All(o => !o.Sold), "refresh replaces sold offers with fresh stock");

                BuyAndCheck(simulation, simulation.GetShopOffers()[0]);
                permissions = OwnedPermissions(simulation);
                Check(simulation.TryCloseShop(morning) && simulation.Day.Phase == DayPhase.WakeUp,
                    "closing the shop returns to the same morning");
                uint beforeReopen = simulation.Day.RandomState;
                Check(simulation.TryOpenShop(morning) && simulation.GetShopOffers()[0].Sold && simulation.Day.RandomState == beforeReopen,
                    "reopening retains sold stock and uses no draws");
                Check(simulation.TryConfirmLoadout(morning) && simulation.TryCloseShop(morning) &&
                    simulation.TryCompleteWakeUp(morning, out error), "production starts after confirming and closing the terminal");
                Check(!simulation.BuyOffer(0) && !simulation.RefreshShop() && !simulation.TryOpenShop(simulation.Day.Version),
                    "production cannot access the shop or reroll stock");
                CompleteDayProduction(simulation);
                Check(simulation.TryCompleteDaySettlement(simulation.Day.Version, out error), error);
                Check(simulation.TryCompleteRest(simulation.Day.Version, out error), error);
                Check(simulation.GetShopOffers().Length == 0, "the next day starts with ungenerated stock");
                uint secondMorning = simulation.Day.RandomState;
                Check(simulation.TryOpenShop(simulation.Day.Version) && CheckOffers(simulation, 4).All(o => !o.Sold),
                    "the next morning generates new stock on its first visit");
                Check(simulation.Day.RandomState == Advance(secondMorning, 4), "new stock continues the run RNG");
                Check(permissions.SequenceEqual(OwnedPermissions(simulation)), "moving to the next day preserves purchased permissions");
                Check(simulation.GetOwnedDevices().Length == 0, "morning purchases grant permissions without installing any device");
            }
        }

        private static void CompleteDayProduction(MarbleSimulation simulation)
        {
            uint before = simulation.Context.Round.RandomState;
            Check(simulation.BeginRound(), "placement starts the round");
            Check(simulation.Context.Round.RandomState == before, "next round continues the shared stream without rewinding");
            // Supply only the score required to pass; the real CashOut/settlement code handles phase changes.
            var round = simulation.Context.Round;
            round.Score = round.TargetScore;
            simulation.Context.Round = round;
            Check(simulation.CashOut(), "eligible round cashes out");
            Check(simulation.Session.Phase == RoundPhase.Won, "settlement completes the day's production");
            Check(simulation.Context.Round.RandomState == before, "settlement without a gamble consumes no random draws");
        }

        private static void BuyAndCheck(MarbleSimulation simulation, ShopOfferSnapshot offer)
        {
            uint before = simulation.Context.Round.RandomState;
            long scrip = simulation.Session.CompanyScripBalance;
            Check(simulation.BuyOffer(offer.Index, offer.DefinitionId), "available permission can be purchased");
            Check(simulation.Session.CompanyScripBalance == scrip - offer.Price, "purchase deducts its displayed price once");
            Check(simulation.GetDailyPegs().Single(peg => peg.DefinitionId == offer.DefinitionId).Permitted,
                "permission makes the device selectable in the daily loadout");
            Check(simulation.GetShopOffers()[offer.Index].Sold, "purchased offer is marked sold");
            scrip = simulation.Session.CompanyScripBalance;
            Check(!simulation.BuyOffer(offer.Index, offer.DefinitionId) && simulation.Session.CompanyScripBalance == scrip,
                "rejected repeat purchase changes no wallet");
            Check(simulation.Context.Round.RandomState == before, "purchase and rejected repeat purchase consume no randomness");
        }

        private static uint[] OwnedPermissions(MarbleSimulation simulation) =>
            simulation.GetDailyPegs().Where(peg => peg.Permitted).Select(peg => peg.DefinitionId).OrderBy(id => id).ToArray();

        internal static DailyLoadoutDefinition CreateLoadout(GameBalance balance, int offerCount)
        {
            var ids = balance.Devices.Select(device => device.Id).ToArray();
            var weights = Enum.GetValues(typeof(DeviceRarity)).Cast<DeviceRarity>().Select(DeviceRarityRules.ShopWeight).ToArray();
            var shop = new DailyShopDefinition(2, 3, offerCount, 200, 0, PegPermissionScope.CampaignPermanent,
                Array.Empty<uint>(), ids, weights);
            return new DailyLoadoutDefinition(ids, Array.Empty<uint>(), Array.Empty<uint>(), ids.Length, 0, LoadoutSelectionMode.Manual, shop);
        }

        private static void StartShopCampaign(MarbleSimulation simulation, GameBalance balance, IMarblePhysics physics,
            uint? seed = null, int offers = 4, int days = 1)
        {
            var loadout = CreateLoadout(balance, offers);
            var definitions = Enumerable.Range(1, days).Select(index => new DayDefinition("shop-" + index, index, 50,
                ProductionFeature.Shop | ProductionFeature.DevicePlacement, null, null, Array.Empty<MorningPrepDefinition>(),
                Array.Empty<DayEventDefinition>(), Array.Empty<DayRuleDefinition>(), loadout, null, 8, 2, false, ProductionSettings.Daily)).ToArray();
            var campaign = new DayCampaignDefinition(DayCampaignDefinition.CurrentSchemaVersion, "shop-validation", 100,
                definitions, Array.Empty<DayRuleDefinition>());
            Check(simulation.TryStartDayCampaign(campaign, physics, out string error, seed), error);
        }

        private static void CatalogShortages(GameBalance original)
        {
            foreach (int available in new[] { 1, 2 })
            {
                var balance = original.Copy();
                balance.Devices = balance.Devices.Take(available).ToArray();
                using (var simulation = new MarbleSimulation(balance))
                {
                    StartShopCampaign(simulation, balance, new EmptyPhysics(), 91);
                    CampaignShopSystem.Generate(simulation.Context, simulation.CurrentDayDefinition.DailyLoadout);
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
