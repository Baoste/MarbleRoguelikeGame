using System;
using UnityEngine.Scripting;

namespace MarblesECS
{
    public enum DeviceItemType { Device }

    [Preserve]
    public sealed class DeviceMetadata
    {
        public string name;
        public string description;
        public long biomassScrapPrice;
        public string rarity;
        public int durability;
        public DeviceItemType type;
        internal DeviceRarity Rarity { get; private set; }

        internal DeviceMetadata Copy() => (DeviceMetadata)MemberwiseClone();

        internal static DeviceMetadata Read(string json, string path)
        {
            var input = ConfigJson.Read(json, path);
            if (input.String("type") != "device") throw input.Error("type");
            string rarity = input.String("rarity", "common", optional: true);
            var tier = rarity switch
            {
                "common" => DeviceRarity.Common,
                "uncommon" => DeviceRarity.Uncommon,
                "rare" => DeviceRarity.Rare,
                "legendary" => DeviceRarity.Legendary,
                _ => throw input.Error("rarity")
            };
            double installPrice = input.Number("biomassScrapPrice", 0, optional: true);
            if (installPrice < (input.Has("biomassScrapPrice") ? 1 : 0) ||
                installPrice > CampaignWalletSystem.MaxBalance || installPrice != Math.Truncate(installPrice))
                throw input.Error("biomassScrapPrice");
            return new DeviceMetadata
            {
                name = input.String("name"),
                description = input.String("description"),
                biomassScrapPrice = (long)installPrice,
                rarity = rarity,
                durability = input.Int("durability", 20, optional: true),
                type = DeviceItemType.Device,
                Rarity = tier
            };
        }
    }
}
