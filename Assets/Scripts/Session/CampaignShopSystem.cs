using System;
using System.Collections.Generic;
namespace MarblesECS
{
    internal static class CampaignShopSystem
    {
        private struct Candidate
        {
            internal ShopItemKind Kind;
            internal uint Id;
            internal long Price;
        }

        internal static void Generate(SimulationContext context)
        {
            var balance = context.Balance;
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            offers.Clear();
            var campaign = context.Campaign;
            var devices = new List<Candidate>(balance.Devices.Length);
            var drugs = new List<Candidate>(balance.Drugs.Length);
            foreach (var definition in balance.Devices)
            {
                var candidate = new Candidate { Kind = ShopItemKind.Device, Id = definition.Id, Price = definition.Price };
                devices.Add(candidate);
            }
            foreach (var definition in balance.Drugs)
            {
                var candidate = new Candidate { Kind = ShopItemKind.Drug, Id = definition.Id, Price = definition.Price };
                drugs.Add(candidate);
            }
            // Stable catalog ordering makes an explicitly supplied test seed reproducible.
            devices.Sort((a, b) => a.Id.CompareTo(b.Id));
            drugs.Sort((a, b) => a.Id.CompareTo(b.Id));
            var used = new HashSet<ulong>();
            int deviceCount = balance.Campaign.ShopOfferCount - balance.Campaign.ShopDrugOfferCount;
            for (int i = 0; i < deviceCount; i++)
                if (!AddRandom(context, offers, devices, used, ref campaign.ShopRandomState)) break;
            for (int i = 0; i < balance.Campaign.ShopDrugOfferCount; i++)
                if (!AddRandom(context, offers, drugs, used, ref campaign.ShopRandomState)) break;
            context.Campaign = campaign;
        }

        private static bool AddRandom(SimulationContext context, Unity.Entities.DynamicBuffer<ShopOfferData> offers,
            List<Candidate> source, HashSet<ulong> used, ref uint randomState)
        {
            var available = new List<Candidate>(source.Count);
            foreach (var candidate in source)
                if (!used.Contains(Key(candidate))) available.Add(candidate);
            if (available.Count == 0) return false;
            MarbleRules.Roll(ref randomState, 0);
            int selection = (int)((ulong)randomState * (uint)available.Count >> 32);
            var chosen = available[selection];
            used.Add(Key(chosen));
            offers.Add(new ShopOfferData { Kind = chosen.Kind, DefinitionId = chosen.Id,
                Price = Price(context, chosen.Price) });
            return true;
        }

        private static ulong Key(Candidate candidate) => ((ulong)candidate.Kind << 32) | candidate.Id;

        internal static bool Buy(SimulationContext context, int index)
        {
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            if (index < 0 || index >= offers.Length) return false;
            var offer = offers[index];
            if (!offer.Sold) offer.Price = Price(context, offer.Kind == ShopItemKind.Device
                ? CampaignStateUtility.Device(context, offer.DefinitionId).Price : CampaignStateUtility.Drug(context, offer.DefinitionId).Price);
            if (offer.Sold || context.Campaign.Coins < offer.Price) return false;
            if (offer.Kind == ShopItemKind.Device)
            {
                var definition = CampaignStateUtility.Device(context, offer.DefinitionId);
                int quantity = definition.PurchaseQuantity;
                if (context.DeviceQuery.CalculateEntityCount() + quantity > context.Balance.Campaign.MaxOwnedDevices ||
                    context.Campaign.NextDeviceId >= int.MaxValue - 100000) return false;
                CampaignStateUtility.CreateDevice(context, CampaignStateUtility.Device(context, offer.DefinitionId), offer.Price);
            }
            else
            {
                var inventory = context.Manager.GetBuffer<DrugInventoryData>(context.PlayerEntity);
                int total = 0;
                int slot = -1;
                for (int i = 0; i < inventory.Length; i++)
                {
                    total += inventory[i].Count;
                    if (inventory[i].DefinitionId == offer.DefinitionId) slot = i;
                }
                if (total >= context.Balance.Campaign.MaxDrugInventory || slot < 0) return false;
                var item = inventory[slot];
                item.Count++;
                inventory[slot] = item;
            }
            // Creating an entity invalidates DynamicBuffer handles; reacquire before committing the offer.
            offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            offer.Sold = true;
            offers[index] = offer;
            var campaign = context.Campaign;
            campaign.Coins -= offer.Price;
            context.Campaign = campaign;
            return true;
        }
        internal static long Price(SimulationContext context, long basis) => Math.Max(1, MarbleRules.RoundScore(basis *
            (AttributeRuntime.Enabled(context) ? AttributeRuntime.Global(context, GameAttribute.SHOP_PRICE_MOD) : 1), CampaignStateUtility.MaxCoins));
    }
}
