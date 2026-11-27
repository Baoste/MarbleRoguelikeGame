namespace MarblesECS.Presentation
{
    public static class DeviceDisplayText
    {
        public static string Rarity(DeviceRarity rarity)
        {
            switch (rarity)
            {
                case DeviceRarity.Rare: return "稀有";
                case DeviceRarity.Uncommon: return "罕见";
                case DeviceRarity.Legendary: return "传奇";
                default: return "普通";
            }
        }
        public static string Durability(OwnedDeviceSnapshot device) =>
            device.Durability == -1 ? "无限耐久" : device.Exhausted ? "耐久 0/" + device.Durability +
            (device.CompletingEffect ? " · 当前效果待完成" : " · 本回合已失效") : "耐久 " + device.RemainingDurability + "/" + device.Durability;
        public static string Details(OwnedDeviceSnapshot device) => Rarity(device.Rarity) + " · " + Durability(device) + " · 出售 " + device.SellPrice + " 血肉残料";
        internal static string SaleTarget(OwnedDeviceSnapshot device) =>
            (device.PairId != 0 ? "整对" : string.Empty) + "「" + device.Name + "」";
    }
}
