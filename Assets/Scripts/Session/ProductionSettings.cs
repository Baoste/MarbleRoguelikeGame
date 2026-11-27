namespace MarblesECS
{
    public enum DeviceSupply : byte { OwnedInventory, SelectedTypes }

    /// <summary>Rules captured at the start of production, independently of the entry point.</summary>
    public readonly struct ProductionSettings
    {
        public DeviceSupply Supply { get; }
        public bool InstallDuringProduction { get; }
        public bool DestroyExhaustedDevices { get; }
        public bool AllowDeviceReturn { get; }
        public bool AllowDeviceSale { get; }
        public bool ClearDevicesOnDaySettlement { get; }

        public ProductionSettings(DeviceSupply supply, bool installDuringProduction,
            bool destroyExhaustedDevices, bool allowDeviceReturn, bool allowDeviceSale, bool clearDevicesOnDaySettlement)
        {
            Supply = supply;
            InstallDuringProduction = installDuringProduction;
            DestroyExhaustedDevices = destroyExhaustedDevices;
            AllowDeviceReturn = allowDeviceReturn;
            AllowDeviceSale = allowDeviceSale;
            ClearDevicesOnDaySettlement = clearDevicesOnDaySettlement;
        }

        public ProductionSettings WithSupply(DeviceSupply supply) =>
            new ProductionSettings(supply, InstallDuringProduction, DestroyExhaustedDevices,
                AllowDeviceReturn, AllowDeviceSale, ClearDevicesOnDaySettlement);

        internal static ProductionSettings Inventory =>
            new ProductionSettings(DeviceSupply.OwnedInventory, false, false, true, true, false);
        internal static ProductionSettings Daily =>
            new ProductionSettings(DeviceSupply.SelectedTypes, true, true, false, false, true);
    }
}
