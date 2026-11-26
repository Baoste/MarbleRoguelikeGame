using System;
using System.Collections.Generic;

namespace MarblesECS
{
    public enum DayPhase : byte { WakeUp, Production, DaySettlement, Rest }
    public enum DayTrigger : byte
    {
        WakeUpEntered, WakeUpExited, ProductionEntered, ProductionExited,
        DaySettlementEntered, DaySettlementExited, RestEntered, RestExited,
        BuildEntered, PlayingEntered, DrainingEntered, ShopEntered, RoundSettled,
        ProductionSucceeded, ProductionFailed, DayRetried, DayCompleted
    }
    public enum DayEventPolicy : byte { OncePerAttempt, EveryOccurrence }
    public enum DayRuleScope : byte { Campaign, Day, Phase, Temporary }

    public sealed class DayCampaignDefinition
    {
        internal const int LegacySchemaVersion = 2;
        internal const int CurrentSchemaVersion = 3;
        public int SchemaVersion { get; }
        public long StartingCompanyScrip { get; }
        public string SequenceId { get; }
        public IReadOnlyList<DayDefinition> Days { get; }
        public bool IsConfigured => Days.Count != 0;
        internal IReadOnlyList<DayRuleDefinition> CampaignRules { get; }

        internal DayCampaignDefinition(int schemaVersion, string id, long startingCompanyScrip,
            DayDefinition[] days, DayRuleDefinition[] campaignRules)
        {
            SchemaVersion = schemaVersion;
            SequenceId = id;
            StartingCompanyScrip = startingCompanyScrip;
            Days = Array.AsReadOnly(days);
            CampaignRules = Array.AsReadOnly(campaignRules);
        }
    }

    public sealed class DayDefinition
    {
        public string DayId { get; }
        public int Order { get; }
        public long TargetScore { get; }
        public long RewardCoins { get; }
        public DailyLoadoutDefinition DailyLoadout { get; }
        public BloodProgressionDefinition BloodProgression { get; }
        public ProductionFeature ProductionFeatures { get; }
        public string BoardLayoutFile { get; }
        public BoardLayoutDefinition BoardLayout { get; }
        public IReadOnlyList<MorningPrepDefinition> MorningPrep { get; }
        public IReadOnlyList<DayEventDefinition> Events { get; }
        public IReadOnlyList<DayRuleDefinition> Rules { get; }

        internal DayDefinition(string id, int order, long targetScore, long rewardCoins, ProductionFeature features,
            string boardLayoutFile, BoardLayoutDefinition boardLayout, MorningPrepDefinition[] morningPrep,
            DayEventDefinition[] events, DayRuleDefinition[] rules, DailyLoadoutDefinition dailyLoadout,
            BloodProgressionDefinition bloodProgression)
        {
            DayId = id;
            Order = order;
            TargetScore = targetScore;
            RewardCoins = rewardCoins;
            DailyLoadout = dailyLoadout;
            BloodProgression = bloodProgression;
            ProductionFeatures = features;
            BoardLayoutFile = boardLayoutFile;
            BoardLayout = boardLayout;
            MorningPrep = Array.AsReadOnly(morningPrep);
            Events = Array.AsReadOnly(events);
            Rules = Array.AsReadOnly(rules);
        }
    }

    public readonly struct BloodValueStage
    {
        public double SpentBlood { get; }
        public double BaseValue { get; }

        internal BloodValueStage(double spentBlood, double baseValue)
        {
            SpentBlood = spentBlood;
            BaseValue = baseValue;
        }
    }

    public sealed class BloodProgressionDefinition
    {
        public float InitialBlood { get; }
        public IReadOnlyList<BloodValueStage> Stages { get; }

        internal BloodProgressionDefinition(float initialBlood, IReadOnlyList<BloodValueStage> stages)
        {
            InitialBlood = initialBlood;
            Stages = stages;
        }

        internal int StageAt(double spentBlood)
        {
            int index = Stages.Count - 1;
            while (index >= 0 && spentBlood < Stages[index].SpentBlood) index--;
            return index;
        }
    }

    public sealed class DayEventDefinition
    {
        public string EventId { get; }
        public DayTrigger Trigger { get; }
        public int Priority { get; }
        public DayEventPolicy Policy { get; }
        internal Func<DayLifecycleNotice, bool> Condition { get; }
        internal IReadOnlyList<DayAction> Actions { get; }

        internal DayEventDefinition(string id, DayTrigger trigger, int priority, DayEventPolicy policy,
            Func<DayLifecycleNotice, bool> condition, DayAction[] actions)
        {
            EventId = id;
            Trigger = trigger;
            Priority = priority;
            Policy = policy;
            Condition = condition;
            Actions = Array.AsReadOnly(actions);
        }
    }

    public sealed class DayRuleDefinition
    {
        public string RuleId { get; }
        public string Type { get; }
        public DayRuleScope Scope { get; }
        public DayPhase? Phase { get; }
        public long DurationTicks { get; }
        internal AttributeEffect? Attribute { get; }
        internal Func<IDayFeature> CreateFeature { get; }

        internal DayRuleDefinition(string id, string type, DayRuleScope scope, DayPhase? phase, long durationTicks,
            AttributeEffect? attribute, Func<IDayFeature> createFeature)
        {
            RuleId = id;
            Type = type;
            Scope = scope;
            Phase = phase;
            DurationTicks = durationTicks;
            Attribute = attribute;
            CreateFeature = createFeature;
        }
    }
}
