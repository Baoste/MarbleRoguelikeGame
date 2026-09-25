namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public bool BuyOffer(int index)
        {
            return CanCampaignAct(RoundPhase.Shop) && CampaignShopSystem.Buy(context, index);
        }

        public bool RefreshShop()
        {
            if (!CanCampaignAct(RoundPhase.Shop)) return false;
            var campaign = context.Campaign;
            long price = CampaignShopSystem.Price(context, context.Balance.Campaign.ShopRefreshCost);
            if (campaign.Coins < price) return false;
            campaign.Coins -= price;
            campaign.ShopRefreshCount++;
            context.Campaign = campaign;
            CampaignShopSystem.Generate(context);
            return true;
        }

        public bool SellDevice(int instanceId)
        {
            if (!CanCampaignAct(RoundPhase.Shop) && !CanCampaignAct(RoundPhase.Build)) return false;
            if (!CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            var campaign = context.Campaign;
            long price = MarbleRules.RoundScore(device.PurchasePrice * context.Balance.Campaign.SellRatio, CampaignStateUtility.MaxCoins);
            campaign.Coins = MarbleRules.AddScore(campaign.Coins, price, CampaignStateUtility.MaxCoins);
            campaign.LayoutRevision++;
            context.Manager.DestroyEntity(entity);
            context.Campaign = campaign;
            return true;
        }

        public bool UseDrug(uint definitionId)
        {
            if (!CanCampaignAct(RoundPhase.Playing)) return false;
            var definition = CampaignStateUtility.Drug(context, definitionId);
            if (definition == null) return false;
            var inventory = context.Manager.GetBuffer<DrugInventoryData>(context.PlayerEntity);
            for (int i = 0; i < inventory.Length; i++)
            {
                var item = inventory[i];
                if (item.DefinitionId != definitionId || item.Count == 0) continue;
                if (definition.Kind == DrugKind.LegacyBounce && !AttributeRuntime.Enabled(context))
                { if (!ApplyBounceDrug(definition.RestitutionMultiplier, definition.DurationSeconds)) return false; }
                else
                {
                    var doses = context.Manager.GetBuffer<ActiveDrugData>(context.PlayerEntity);
                    if (definition.Kind == DrugKind.LegacyBounce)
                    {
                        int active = 0;
                        for (int d = 0; d < doses.Length; d++)
                            if (doses[d].EndsAt > context.Round.Time &&
                                CampaignStateUtility.Drug(context, doses[d].DefinitionId).Kind == DrugKind.LegacyBounce) active++;
                        if (active >= context.Tuning.MaxActiveBounceDrugs) return false;
                    }
                    if (!definition.Stackable)
                        for (int d = doses.Length - 1; d >= 0; d--)
                            if (doses[d].DefinitionId == definitionId) doses.RemoveAt(d);
                    doses.Add(new ActiveDrugData { DefinitionId = definitionId, EndsAt = context.Round.Time + definition.DurationSeconds });
                }
                item.Count--;
                inventory = context.Manager.GetBuffer<DrugInventoryData>(context.PlayerEntity);
                inventory[i] = item;
                return true;
            }
            return false;
        }
    }
}
