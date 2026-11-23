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
        public int SchemaVersion => 1;
        public string SequenceId { get; }
        public IReadOnlyList<DayDefinition> Days { get; }
        public bool IsConfigured => Days.Count != 0;
        internal IReadOnlyList<DayRuleDefinition> CampaignRules { get; }

        internal DayCampaignDefinition(string id, DayDefinition[] days, DayRuleDefinition[] campaignRules)
        {
            SequenceId = id;
            Days = Array.AsReadOnly(days);
            CampaignRules = Array.AsReadOnly(campaignRules);
        }
    }

    public sealed class DayDefinition
    {
        public string DayId { get; }
        public int Order { get; }
        public DayProductionPlan ProductionPlan { get; }
        public ProductionFeature ProductionFeatures { get; }
        public IReadOnlyList<MorningPrepDefinition> MorningPrep { get; }
        public IReadOnlyList<DayEventDefinition> Events { get; }
        public IReadOnlyList<DayRuleDefinition> Rules { get; }

        internal DayDefinition(string id, int order, DayProductionPlan plan, ProductionFeature features,
            MorningPrepDefinition[] morningPrep, DayEventDefinition[] events, DayRuleDefinition[] rules)
        {
            DayId = id;
            Order = order;
            ProductionPlan = plan;
            ProductionFeatures = features;
            MorningPrep = Array.AsReadOnly(morningPrep);
            Events = Array.AsReadOnly(events);
            Rules = Array.AsReadOnly(rules);
        }
    }

    public sealed class DayProductionPlan
    {
        public string PlanId { get; }
        public uint FirstStageId { get; }
        public uint LastStageId { get; }
        public long? TargetScore { get; }

        internal DayProductionPlan(string id, uint firstStageId, uint lastStageId, long? targetScore)
        {
            PlanId = id;
            FirstStageId = firstStageId;
            LastStageId = lastStageId;
            TargetScore = targetScore;
        }

        internal (int first, int end) Resolve(GameBalance balance)
        {
            var stages = balance.Stages;
            int first = Array.FindIndex(stages, stage => stage.Id == FirstStageId);
            int last = Array.FindIndex(stages, stage => stage.Id == LastStageId);
            if (first < 0) throw new ArgumentException($"Production plan '{PlanId}'.firstStageId: unknown stage {FirstStageId}.");
            if (last < 0) throw new ArgumentException($"Production plan '{PlanId}'.lastStageId: unknown stage {LastStageId}.");
            if (last < first) throw new ArgumentException($"Production plan '{PlanId}'.lastStageId precedes firstStageId.");
            if (TargetScore.HasValue && (first != last || TargetScore.Value <= 0 || TargetScore.Value > balance.Campaign.MaxTotalScore))
                throw new ArgumentException($"Production plan '{PlanId}'.targetScore requires a single stage and a positive value within MaxTotalScore.");
            return (first, last + 1);
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
