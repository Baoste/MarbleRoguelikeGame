using System;

namespace MarblesECS
{
    public static class DeviceRarityRules
    {
        // Tier weights are independent of how many devices belong to each tier.
        public static int ShopWeight(DeviceRarity rarity)
        {
            switch (rarity)
            {
                case DeviceRarity.Common: return 60;
                case DeviceRarity.Uncommon: return 30;
                case DeviceRarity.Rare: return 7;
                case DeviceRarity.Legendary: return 3;
                default: throw new ArgumentOutOfRangeException(nameof(rarity));
            }
        }
        public static long StandardPurchasePrice(DeviceRarity rarity)
        {
            switch (rarity)
            {
                case DeviceRarity.Common: return 9;
                case DeviceRarity.Uncommon: return 14;
                case DeviceRarity.Rare: return 23;
                case DeviceRarity.Legendary: return 38;
                default: throw new ArgumentOutOfRangeException(nameof(rarity));
            }
        }
    }
}
