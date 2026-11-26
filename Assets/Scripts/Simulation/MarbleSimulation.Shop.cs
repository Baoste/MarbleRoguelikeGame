namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public bool ShopOpen => dayFlow != null && dayFlow.ShopOpen;
        public bool ShopCommandsAvailable => CanChangeShop && dayFlow.AllowsShopping;
        private bool CanChangeShop => !insidePhysicsStep && !startingDay && dayFlow != null;

        public bool TryOpenShop(long version) => CanChangeShop && dayFlow.TryOpenShop(version);
        public bool TryCloseShop(long version) => CanChangeShop && dayFlow.TryCloseShop(version);

        public bool BuyOffer(int index, uint expectedId = 0)
        {
            ThrowIfDisposed();
            return ShopCommandsAvailable && CampaignShopSystem.Buy(context, index, CurrentDayDefinition.DailyLoadout, expectedId);
        }

        public bool CanRefreshDailyShop => ShopCommandsAvailable && CurrentDayDefinition.DailyLoadout != null &&
            CampaignShopSystem.CanRefresh(context, CurrentDayDefinition.DailyLoadout);

        public bool RefreshShop()
        {
            ThrowIfDisposed();
            if (!ShopCommandsAvailable) return false;
            var loadout = CurrentDayDefinition.DailyLoadout;
            if (loadout != null)
            {
                if (!CanRefreshDailyShop ||
                    !CampaignWalletSystem.TrySpend(context, loadout.Shop.Currency, loadout.Shop.RefreshCost.Value)) return false;
                var updated = context.Campaign;
                updated.ShopRefreshCount++;
                context.Campaign = updated;
                CampaignShopSystem.Generate(context, loadout);
                return true;
            }
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
            if (context.Campaign.UsesDailyLoadout) return false;
            if (!ShopCommandsAvailable && !CanEditDevices) return false;
            if (!CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            var campaign = context.Campaign;
            long price = CampaignStateUtility.SellPrice(CampaignStateUtility.Device(context, device.DefinitionId), device);
            campaign.Coins = MarbleRules.AddScore(campaign.Coins, price, CampaignStateUtility.MaxCoins);
            context.Campaign = campaign;
            CampaignStateUtility.DestroyDevice(context, entity);
            CampaignStateUtility.RestockStartingDevice(context, device.DefinitionId);
            return true;
        }
    }
}
