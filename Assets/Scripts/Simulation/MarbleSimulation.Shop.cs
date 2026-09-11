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
            long price = context.Balance.Campaign.ShopRefreshCost;
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
                if (!ApplyBounceDrug(definition.RestitutionMultiplier, definition.DurationSeconds)) return false;
                item.Count--;
                inventory = context.Manager.GetBuffer<DrugInventoryData>(context.PlayerEntity);
                inventory[i] = item;
                return true;
            }
            return false;
        }
    }
}
