using System;
using System.Collections.Generic;
using System.Linq;

namespace MarblesECS
{
    public enum LoadoutSelectionMode : byte { Manual, AutoSelectAvailable }
    public enum PegPermissionScope : byte { CampaignPermanent, DayOnly, RunOnly }

    public sealed class DailyLoadoutDefinition
    {
        public IReadOnlyList<uint> UnlockedPegIds { get; }
        public IReadOnlyList<uint> DefaultPegIds { get; }
        public IReadOnlyList<uint> RequiredPegIds { get; }
        public int MaxLoadoutTypeCount { get; }
        public int MinLoadoutTypeCount { get; }
        public LoadoutSelectionMode SelectionMode { get; }
        public DailyShopDefinition Shop { get; }

        public DailyLoadoutDefinition(uint[] unlocked, uint[] defaults, uint[] required, int max, int min,
            LoadoutSelectionMode mode, DailyShopDefinition shop)
        {
            if (unlocked == null || defaults == null || required == null)
                throw new ArgumentException("A loadout requires unlocked, default and required ID lists.");
            if (max < 1 || max > unlocked.Length || max > 200 || min < 0 || min > max || required.Length > max)
                throw new ArgumentException("Loadout type limits are out of range.");
            if (unlocked.Distinct().Count() != unlocked.Length || defaults.Distinct().Count() != defaults.Length ||
                required.Distinct().Count() != required.Length || defaults.Except(unlocked).Any() || required.Except(defaults).Any())
                throw new ArgumentException("Loadout IDs must be unique. Required IDs must be defaults, and defaults must be unlocked.");
            if (!Enum.IsDefined(typeof(LoadoutSelectionMode), mode))
                throw new ArgumentException("Invalid loadout selection mode.");
            UnlockedPegIds = Array.AsReadOnly((uint[])unlocked.Clone());
            DefaultPegIds = Array.AsReadOnly((uint[])defaults.Clone());
            RequiredPegIds = Array.AsReadOnly((uint[])required.Clone());
            MaxLoadoutTypeCount = max;
            MinLoadoutTypeCount = min;
            SelectionMode = mode;
            Shop = shop;
        }
    }

    public sealed class DailyShopDefinition
    {
        public GameCurrency Currency => GameCurrency.CompanyScrip;
        public long DrawCost { get; }
        public long? RefreshCost { get; }
        public int OfferCount { get; }
        public int SelectionLimit { get; }
        public uint RandomSeed { get; }
        public PegPermissionScope PermissionScope { get; }
        public IReadOnlyList<uint> GuaranteedPegIds { get; }
        public IReadOnlyList<uint> RandomPool { get; }
        public IReadOnlyList<int> RarityWeights { get; }

        internal DailyShopDefinition(long drawCost, long? refreshCost, int offers, int limit, uint seed,
            PegPermissionScope scope, uint[] guaranteed, uint[] pool, int[] weights)
        {
            DrawCost = drawCost;
            RefreshCost = refreshCost;
            OfferCount = offers;
            SelectionLimit = limit;
            RandomSeed = seed;
            PermissionScope = scope;
            GuaranteedPegIds = Array.AsReadOnly(guaranteed);
            RandomPool = Array.AsReadOnly(pool);
            RarityWeights = Array.AsReadOnly(weights);
        }
    }
}
