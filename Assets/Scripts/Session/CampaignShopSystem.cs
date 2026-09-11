namespace MarblesECS
{
    internal static class CampaignShopSystem
    {
        internal static void Generate(SimulationContext context)
        {
            var balance = context.Balance;
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            offers.Clear();
            var campaign = context.Campaign;
            int count = balance.Devices.Length + balance.Drugs.Length;
            for (int i = 0; i < balance.Campaign.ShopOfferCount; i++)
            {
                MarbleRules.Roll(ref campaign.RandomState, 0);
                int selection = (int)((ulong)campaign.RandomState * (uint)count >> 32);
                if (selection < balance.Devices.Length)
                {
                    var definition = balance.Devices[selection];
                    offers.Add(new ShopOfferData { Kind = ShopItemKind.Device, DefinitionId = definition.Id, Price = definition.Price });
                }
                else
                {
                    var definition = balance.Drugs[selection - balance.Devices.Length];
                    offers.Add(new ShopOfferData { Kind = ShopItemKind.Drug, DefinitionId = definition.Id, Price = definition.Price });
                }
            }
            context.Campaign = campaign;
        }

        internal static bool Buy(SimulationContext context, int index)
        {
            var offers = context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity);
            if (index < 0 || index >= offers.Length) return false;
            var offer = offers[index];
            if (offer.Sold || context.Campaign.Coins < offer.Price) return false;
            if (offer.Kind == ShopItemKind.Device)
            {
                if (context.DeviceQuery.CalculateEntityCount() >= context.Balance.Campaign.MaxOwnedDevices ||
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
    }
}
