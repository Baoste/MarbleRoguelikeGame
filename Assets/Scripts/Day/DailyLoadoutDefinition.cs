using System;
using System.Collections.Generic;

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
        public long InitialBiomassScrap { get; }
        public long ClearCompanyScripReward { get; }
        public DailyShopDefinition Shop { get; }

        internal DailyLoadoutDefinition(uint[] unlocked, uint[] defaults, uint[] required, int max, int min,
            LoadoutSelectionMode mode, long initialScrap, long reward, DailyShopDefinition shop)
        {
            UnlockedPegIds = Array.AsReadOnly(unlocked);
            DefaultPegIds = Array.AsReadOnly(defaults);
            RequiredPegIds = Array.AsReadOnly(required);
            MaxLoadoutTypeCount = max;
            MinLoadoutTypeCount = min;
            SelectionMode = mode;
            InitialBiomassScrap = initialScrap;
            ClearCompanyScripReward = reward;
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
