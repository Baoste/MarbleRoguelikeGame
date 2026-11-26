namespace MarblesECS
{
    public enum DeviceSupply : byte { OwnedInventory, SelectedTypes }

    /// <summary>Rules captured at the start of production, independently of the entry point.</summary>
    public readonly struct ProductionSettings
    {
        public DeviceSupply Supply { get; }
        public GameCurrency Currency { get; }
        public bool InstallDuringProduction { get; }
        public bool DestroyExhaustedDevices { get; }
        public bool AllowDeviceReturn { get; }
        public bool AllowDeviceSale { get; }
        public bool ClearDevicesOnDaySettlement { get; }

        public ProductionSettings(DeviceSupply supply, GameCurrency currency, bool installDuringProduction,
            bool destroyExhaustedDevices, bool allowDeviceReturn, bool allowDeviceSale, bool clearDevicesOnDaySettlement)
        {
            Supply = supply;
            Currency = currency;
            InstallDuringProduction = installDuringProduction;
            DestroyExhaustedDevices = destroyExhaustedDevices;
            AllowDeviceReturn = allowDeviceReturn;
            AllowDeviceSale = allowDeviceSale;
            ClearDevicesOnDaySettlement = clearDevicesOnDaySettlement;
        }

        public ProductionSettings WithSupply(DeviceSupply supply) =>
            new ProductionSettings(supply, Currency, InstallDuringProduction, DestroyExhaustedDevices,
                AllowDeviceReturn, AllowDeviceSale, ClearDevicesOnDaySettlement);

        internal static ProductionSettings Inventory =>
            new ProductionSettings(DeviceSupply.OwnedInventory, GameCurrency.Coins, false, false, true, true, false);
        internal static ProductionSettings Daily =>
            new ProductionSettings(DeviceSupply.SelectedTypes, GameCurrency.BiomassScrap, true, true, false, false, true);
    }
}
