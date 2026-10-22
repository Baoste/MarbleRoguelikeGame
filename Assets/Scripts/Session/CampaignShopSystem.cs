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
            var all = new List<Candidate>(balance.Devices.Length);
            var rushDevices = new List<Candidate>();
            foreach (var definition in balance.Devices)
            {
                var candidate = new Candidate { Kind = ShopItemKind.Device, Id = definition.Id, Price = definition.Price };
                all.Add(candidate);
                if (definition.RushCategory || definition.RushChanceAdd > 0) rushDevices.Add(candidate);
            }

            var used = new HashSet<ulong>();
            if (offers.Length < balance.Campaign.ShopOfferCount) AddRandom(context, offers, rushDevices, used, ref campaign.RandomState);
            while (offers.Length < balance.Campaign.ShopOfferCount && AddRandom(context, offers, all, used, ref campaign.RandomState)) { }
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
            if (offer.Kind != ShopItemKind.Device || offer.Sold) return false;
            var definition = CampaignStateUtility.Device(context, offer.DefinitionId);
            if (definition == null) return false;
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
        internal static long Price(SimulationContext context, long basis) => Math.Max(1, MarbleRules.RoundScore(basis *
            (AttributeRuntime.Enabled(context) ? AttributeRuntime.Global(context, GameAttribute.SHOP_PRICE_MOD) : 1), CampaignStateUtility.MaxCoins));
    }
}
