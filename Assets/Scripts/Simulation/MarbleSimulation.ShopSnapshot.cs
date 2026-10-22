using System;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public ShopOfferSnapshot[] GetShopOffers()
        {
            ThrowIfDisposed();
            if (context.Balance == null) return Array.Empty<ShopOfferSnapshot>();
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            var result = new ShopOfferSnapshot[offers.Length];
            for (int i = 0; i < offers.Length; i++)
            {
                var offer = offers[i];
                string name, description;
                long basePrice;
                if (offer.Kind == ShopItemKind.Device)
                {
                    var definition = CampaignStateUtility.Device(context, offer.DefinitionId);
                    name = definition.Name; description = definition.Description; basePrice = definition.Price;
                }
                else throw new InvalidOperationException("Unsupported shop item kind.");
                if (!offer.Sold) offer.Price = CampaignShopSystem.Price(context, basePrice);
                result[i] = new ShopOfferSnapshot
                {
                    Index = i, Kind = offer.Kind, DefinitionId = offer.DefinitionId,
                    Name = name, Description = description, Price = offer.Price, Sold = offer.Sold
                };
                if (offer.Kind == ShopItemKind.Device)
                {
                    var definition = CampaignStateUtility.Device(context, offer.DefinitionId);
                    result[i].Rarity = definition.Rarity; result[i].Durability = definition.Durability;
                    result[i].SellPrice = definition.SellPrice;
                }
            }
            return result;
        }


    }
}
