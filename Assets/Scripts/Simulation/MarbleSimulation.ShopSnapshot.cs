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
            bool daily = CurrentDayDefinition?.DailyLoadout != null;
            var result = new ShopOfferSnapshot[offers.Length];
            for (int i = 0; i < offers.Length; i++)
            {
                var offer = offers[i];
                if (offer.Kind != ShopItemKind.Device) throw new InvalidOperationException("Unsupported shop item kind.");
                var definition = CampaignStateUtility.Device(context, offer.DefinitionId);
                if (!daily && !offer.Sold) offer.Price = CampaignShopSystem.Price(context, definition.Price);
                result[i] = new ShopOfferSnapshot
                {
                    Index = i, Kind = offer.Kind, DefinitionId = offer.DefinitionId,
                    Name = definition.Name, Description = definition.Description, Price = offer.Price, Sold = offer.Sold,
                    Rarity = definition.Rarity, Durability = definition.Durability,
                    Selected = daily && DailyLoadoutSystem.IsSelected(context, offer.DefinitionId)
                };
            }
            return result;
        }


    }
}
