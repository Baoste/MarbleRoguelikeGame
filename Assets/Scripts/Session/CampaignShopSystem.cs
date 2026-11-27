using System;
using System.Collections.Generic;
using System.Linq;
namespace MarblesECS
{
    internal static class CampaignShopSystem
    {
        internal static void Generate(SimulationContext context, DailyLoadoutDefinition loadout)
        {
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            offers.Clear();
            var shop = loadout.Shop;
            if (shop == null || !context.HasProductionFeature(ProductionFeature.Shop)) return;
            var round = context.Round;
            var devices = context.Balance.Devices.Where(definition => shop.RandomPool.Contains(definition.Id) &&
                !DailyLoadoutSystem.HasPermission(context, loadout, definition.Id)).ToList();
            // Stable catalog ordering makes an explicitly supplied test seed reproducible.
            devices.Sort((a, b) => a.Id.CompareTo(b.Id));
            var used = new HashSet<uint>();
            foreach (uint id in shop.GuaranteedPegIds)
            {
                if (DailyLoadoutSystem.HasPermission(context, loadout, id)) continue;
                MarbleRules.NextRandom(ref round.RandomState);
                used.Add(id);
                offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = id, Price = shop.DrawCost });
            }
            while (offers.Length < shop.OfferCount && AddRandom(offers, devices, used, ref round.RandomState, shop)) { }
            context.Round = round;
        }

        private static bool AddRandom(Unity.Entities.DynamicBuffer<ShopOfferData> offers,
            List<DeviceDefinition> source, HashSet<uint> used, ref uint randomState, DailyShopDefinition shop)
        {
            var available = new List<DeviceDefinition>(source.Count);
            var counts = new int[4];
            foreach (var candidate in source)
                if (!used.Contains(candidate.Id))
                { available.Add(candidate); counts[(int)candidate.Rarity]++; }
            if (available.Count == 0) return false;
            int totalWeight = 0;
            for (int tier = 0; tier < counts.Length; tier++)
                if (counts[tier] > 0) totalWeight += shop.RarityWeights[tier];
            // One draw selects a tier and an equally likely item within it. Missing/exhausted
            // tiers have no weight; there are no retries, blank slots or extra RNG streams.
            MarbleRules.NextRandom(ref randomState);
            uint sample = unchecked(randomState + shop.RandomSeed);
            double selection = sample / 4294967296d * totalWeight;
            DeviceDefinition chosen = null;
            for (int tier = 0; tier < counts.Length; tier++)
            {
                if (counts[tier] == 0) continue;
                int weight = shop.RarityWeights[tier];
                if (selection >= weight) { selection -= weight; continue; }
                int index = (int)(selection / weight * counts[tier]);
                foreach (var candidate in available)
                    if ((int)candidate.Rarity == tier && index-- == 0) { chosen = candidate; break; }
                break;
            }
            used.Add(chosen.Id);
            offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = chosen.Id, Price = shop.DrawCost });
            return true;
        }

        internal static bool Buy(SimulationContext context, int index, DailyLoadoutDefinition loadout, uint expectedId = 0)
        {
            var shop = loadout.Shop;
            if (shop == null || !context.HasProductionFeature(ProductionFeature.Shop)) return false;
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            if (index < 0 || index >= offers.Length) return false;
            var offer = offers[index];
            if (offer.Kind != ShopItemKind.Device || offer.Sold || (expectedId != 0 && offer.DefinitionId != expectedId)) return false;
            if (CampaignStateUtility.Device(context, offer.DefinitionId) == null ||
                DailyLoadoutSystem.State(context).ShopDraws >= shop.SelectionLimit ||
                DailyLoadoutSystem.HasPermission(context, loadout, offer.DefinitionId) ||
                !CampaignWalletSystem.TrySpend(context, shop.Currency, offer.Price)) return false;
            DailyLoadoutSystem.RecordPurchase(context, offer.DefinitionId, shop.PermissionScope);
            offer.Sold = true;
            offers[index] = offer;
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
    }
}
