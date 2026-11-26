using System;
using System.Collections.Generic;
using System.Linq;
namespace MarblesECS
{
    internal static class CampaignShopSystem
    {
        private struct Candidate
        {
            internal ShopItemKind Kind;
            internal DeviceRarity Rarity;
            internal uint Id;
            internal long Price;
        }

        internal static void Generate(SimulationContext context, DailyLoadoutDefinition loadout = null)
        {
            var balance = context.Balance;
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            offers.Clear();
            if (!context.HasProductionFeature(ProductionFeature.Shop)) return;
            var round = context.Round;
            var shop = loadout?.Shop;
            var devices = new List<Candidate>(balance.Devices.Length);
            foreach (var definition in balance.Devices)
            {
                if (loadout != null && (!shop.RandomPool.Contains(definition.Id) ||
                    DailyLoadoutSystem.HasPermission(context, loadout, definition.Id))) continue;
                var candidate = new Candidate { Kind = ShopItemKind.Device, Id = definition.Id, Price = definition.Price, Rarity = definition.Rarity };
                devices.Add(candidate);
            }
            // Stable catalog ordering makes an explicitly supplied test seed reproducible.
            devices.Sort((a, b) => a.Id.CompareTo(b.Id));
            var used = new HashSet<ulong>();
            if (shop != null)
                foreach (uint id in shop.GuaranteedPegIds)
                {
                    if (DailyLoadoutSystem.HasPermission(context, loadout, id)) continue;
                    MarbleRules.NextRandom(ref round.RandomState);
                    used.Add(id);
                    offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = id, Price = shop.DrawCost });
                }
            int count = shop?.OfferCount ?? balance.Campaign.ShopOfferCount;
            while (offers.Length < count && AddRandom(context, offers, devices, used, ref round.RandomState, shop)) { }
            context.Round = round;
        }

        private static bool AddRandom(SimulationContext context, Unity.Entities.DynamicBuffer<ShopOfferData> offers,
            List<Candidate> source, HashSet<ulong> used, ref uint randomState, DailyShopDefinition shop)
        {
            var available = new List<Candidate>(source.Count);
            var counts = new int[4];
            foreach (var candidate in source)
                if (!used.Contains(Key(candidate)))
                { available.Add(candidate); counts[(int)candidate.Rarity]++; }
            if (available.Count == 0) return false;
            int totalWeight = 0;
            for (int tier = 0; tier < counts.Length; tier++)
                if (counts[tier] > 0) totalWeight += Weight(shop, tier);
            // One draw selects a tier and an equally likely item within it. Missing/exhausted
            // tiers have no weight; there are no retries, blank slots or extra RNG streams.
            MarbleRules.NextRandom(ref randomState);
            uint sample = unchecked(randomState + (shop?.RandomSeed ?? 0));
            double selection = sample / 4294967296d * totalWeight;
            var chosen = default(Candidate);
            for (int tier = 0; tier < counts.Length; tier++)
            {
                if (counts[tier] == 0) continue;
                int weight = Weight(shop, tier);
                if (selection >= weight) { selection -= weight; continue; }
                int index = (int)(selection / weight * counts[tier]);
                foreach (var candidate in available)
                    if ((int)candidate.Rarity == tier && index-- == 0) { chosen = candidate; break; }
                break;
            }
            used.Add(Key(chosen));
            offers.Add(new ShopOfferData { Kind = chosen.Kind, DefinitionId = chosen.Id,
                Price = shop?.DrawCost ?? Price(context, chosen.Price) });
            return true;
        }

        private static int Weight(DailyShopDefinition shop, int tier) => shop == null
            ? DeviceRarityRules.ShopWeight((DeviceRarity)tier) : shop.RarityWeights[tier];

        private static ulong Key(Candidate candidate) => ((ulong)candidate.Kind << 32) | candidate.Id;

        internal static bool Buy(SimulationContext context, int index, DailyLoadoutDefinition loadout = null, uint expectedId = 0)
        {
            if (!context.HasProductionFeature(ProductionFeature.Shop)) return false;
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            if (index < 0 || index >= offers.Length) return false;
            var offer = offers[index];
            if (offer.Kind != ShopItemKind.Device || offer.Sold || (expectedId != 0 && offer.DefinitionId != expectedId)) return false;
            var definition = CampaignStateUtility.Device(context, offer.DefinitionId);
            if (definition == null) return false;
            if (loadout != null)
            {
                var shop = loadout.Shop;
                if (shop == null || DailyLoadoutSystem.State(context).ShopDraws >= shop.SelectionLimit ||
                    DailyLoadoutSystem.HasPermission(context, loadout, offer.DefinitionId) ||
                    !CampaignWalletSystem.TrySpend(context, shop.Currency, offer.Price)) return false;
                DailyLoadoutSystem.RecordPurchase(context, offer.DefinitionId, shop.PermissionScope);
                offer.Sold = true;
                offers[index] = offer;
                return true;
            }
            offer.Price = Price(context, definition.Price);
            if (context.Campaign.Coins < offer.Price) return false;
            int quantity = definition.PurchaseQuantity;
            if (context.DeviceQuery.CalculateEntityCount() + quantity > context.Balance.Campaign.MaxOwnedDevices ||
                context.Campaign.NextDeviceId >= int.MaxValue - 100000) return false;
            CampaignStateUtility.CreateDevice(context, definition, offer.Price);
            // Creating an entity invalidates DynamicBuffer handles; reacquire before committing the offer.
            offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            offer.Sold = true;
            offers[index] = offer;
            var campaign = context.Campaign;
            campaign.Coins -= offer.Price;
            context.Campaign = campaign;
            return true;
        }

        internal static bool CanRefresh(SimulationContext context, DailyLoadoutDefinition loadout)
        {
            var shop = loadout.Shop;
            if (shop?.RefreshCost == null || DailyLoadoutSystem.State(context).ShopDraws >= shop.SelectionLimit ||
                !CampaignWalletSystem.CanSpend(context, shop.Currency, shop.RefreshCost.Value)) return false;
            int remaining = shop.RandomPool.Union(shop.GuaranteedPegIds)
                .Count(id => !DailyLoadoutSystem.HasPermission(context, loadout, id));
            int visible = 0;
            foreach (var offer in context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity))
                if (!offer.Sold) visible++;
            return remaining > visible;
        }
        internal static long Price(SimulationContext context, long basis) => Math.Max(1, MarbleRules.RoundScore(basis *
            (AttributeRuntime.Enabled(context) ? AttributeRuntime.Global(context, GameAttribute.SHOP_PRICE_MOD) : 1), CampaignStateUtility.MaxCoins));
    }
}
