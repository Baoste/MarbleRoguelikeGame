namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public bool ShopOpen => dayFlow != null && dayFlow.ShopOpen;
        public bool ShopCommandsAvailable => CanChangeShop && dayFlow.AllowsShopping;
        private bool CanChangeShop => !insidePhysicsStep && !startingDay && dayFlow != null;

        public bool TryOpenShop(long version) => CanChangeShop && dayFlow.TryOpenShop(version);
        public bool TryCloseShop(long version) => CanChangeShop && dayFlow.TryCloseShop(version);

        public bool BuyOffer(int index)
        {
            ThrowIfDisposed();
            return ShopCommandsAvailable && CampaignShopSystem.Buy(context, index);
        }

        public bool RefreshShop()
        {
            ThrowIfDisposed();
            if (!ShopCommandsAvailable) return false;
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
            if (!ShopCommandsAvailable && !CanEditDevices) return false;
            if (!CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            var campaign = context.Campaign;
            long price = CampaignStateUtility.Device(context, device.DefinitionId).SellPrice;
            campaign.Coins = MarbleRules.AddScore(campaign.Coins, price, CampaignStateUtility.MaxCoins);
            context.Campaign = campaign;
            CampaignStateUtility.DestroyDevice(context, entity);
            return true;
        }
    }
}
