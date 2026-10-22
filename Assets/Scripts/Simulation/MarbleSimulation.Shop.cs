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
            long price = CampaignStateUtility.Device(context, device.DefinitionId).SellPrice;
            campaign.Coins = MarbleRules.AddScore(campaign.Coins, price, CampaignStateUtility.MaxCoins);
            campaign.LayoutRevision++;
            context.Manager.DestroyEntity(entity);
            context.DeviceScripts.Remove(instanceId);
            context.Transients.RemoveSource(context, instanceId);
            context.Campaign = campaign;
            return true;
        }


    }
}
