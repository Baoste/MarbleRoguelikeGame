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
                else
                {
                    var definition = CampaignStateUtility.Drug(context, offer.DefinitionId);
                    name = definition.Name; description = definition.Description; basePrice = definition.Price;
                }
                if (!offer.Sold) offer.Price = CampaignShopSystem.Price(context, basePrice);
                result[i] = new ShopOfferSnapshot
                {
                    Index = i, Kind = offer.Kind, DefinitionId = offer.DefinitionId,
                    Name = name, Description = description, Price = offer.Price, Sold = offer.Sold
                };
            }
            return result;
        }

        public DrugInventorySnapshot[] GetDrugInventory()
        {
            ThrowIfDisposed();
            if (context.Balance == null) return Array.Empty<DrugInventorySnapshot>();
            var inventory = context.Manager.GetBuffer<DrugInventoryData>(context.PlayerEntity);
            var result = new DrugInventorySnapshot[inventory.Length];
            for (int i = 0; i < inventory.Length; i++)
            {
                var item = inventory[i];
                var definition = CampaignStateUtility.Drug(context, item.DefinitionId);
                result[i] = new DrugInventorySnapshot
                {
                    DefinitionId = item.DefinitionId, Name = definition.Name, Count = item.Count, Description = definition.Description,
                    RestitutionMultiplier = definition.RestitutionMultiplier, DurationSeconds = definition.DurationSeconds
                };
                var doses = context.Manager.GetBuffer<ActiveDrugData>(context.PlayerEntity);
                for (int d = 0; d < doses.Length; d++)
                    if (doses[d].DefinitionId == item.DefinitionId && doses[d].EndsAt > context.Round.Time)
                    { result[i].ActiveDoses++; result[i].SecondsRemaining = Math.Max(result[i].SecondsRemaining, doses[d].EndsAt - context.Round.Time); }
            }
            return result;
        }
    }
}
