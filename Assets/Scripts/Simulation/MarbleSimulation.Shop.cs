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
            if (!CanRefreshDailyShop) return false;
            var loadout = CurrentDayDefinition.DailyLoadout;
            if (!CampaignWalletSystem.TrySpend(context, loadout.Shop.Currency, loadout.Shop.RefreshCost.Value)) return false;
            var campaign = context.Campaign;
            campaign.ShopRefreshCount++;
            context.Campaign = campaign;
            CampaignShopSystem.Generate(context, loadout);
            return true;
        }

        public bool SellDevice(int instanceId)
        {
            if (!context.Campaign.Production.AllowDeviceSale) return false;
            if (!ShopCommandsAvailable && !CanEditDevices) return false;
            if (!CampaignStateUtility.FindDevice(context, instanceId, out var entity)) return false;
            var device = context.Manager.GetComponentData<OwnedDeviceData>(entity);
            long price = CampaignStateUtility.SalePrice(context, device);
            CampaignWalletSystem.Credit(context, GameCurrency.BiomassScrap, price);
            CampaignStateUtility.DestroyDevice(context, entity);
            return true;
        }
    }
}
