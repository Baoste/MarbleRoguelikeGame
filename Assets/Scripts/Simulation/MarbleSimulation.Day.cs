using System;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        private DayFlowSystem dayFlow;
        private bool startingDay;
        internal event Action DayBoardResetting;
        internal event Action DayBoardRestored;

        public DayProgressSnapshot Day => dayFlow == null ? default : dayFlow.Snapshot;
        public DayDefinition CurrentDayDefinition => dayFlow?.Current;
        public bool HasProductionFeature(ProductionFeature feature) => context.HasProductionFeature(feature);
        internal bool HasDayFlow => dayFlow != null;
        public bool ProductionCommandsAvailable => !startingDay && (dayFlow == null || dayFlow.AllowsProduction);
        internal bool CanSimulateProduction => ProductionCommandsAvailable && context.IsRunning;

        public bool TryStartDayCampaign(DayCampaignDefinition definition, IMarblePhysics physics, out string error,
            uint? randomSeed = null, DayRetryRandomPolicy retryRandom = null) =>
            TryStartDayCampaign(definition, physics, out error, randomSeed, retryRandom, false);

        internal bool TryStartDayCampaign(DayCampaignDefinition definition, IMarblePhysics physics, out string error,
            uint? randomSeed, DayRetryRandomPolicy retryRandom, bool grantAllStartingDevices)
        {
            ThrowIfDisposed();
            if (dayFlow != null || startingDay || insidePhysicsStep)
            { error = "A Day campaign can only start once at a simulation step boundary."; return false; }
            if (context.Balance == null)
            { error = "Day campaigns require the existing GameBalance production session."; return false; }
            if (definition == null || !definition.IsConfigured)
            { error = "The Day sequence is unconfigured. Add at least one Day before starting."; return false; }
            if (!(physics is IMarbleDayPhysics dayPhysics) || (started && !ReferenceEquals(physics, context.Physics)))
            { error = "Use the current physics adapter with Day checkpoint support."; return false; }

            var ranges = new (int first, int end)[definition.Days.Count];
            try
            {
                ValidateRuleAttributes(definition.CampaignRules);
                for (int i = 0; i < ranges.Length; i++)
                {
                    var day = definition.Days[i];
                    ranges[i] = day.ProductionPlan.Resolve(context.Balance);
                    ValidateRuleAttributes(day.Rules);
                }
            }
            catch (ArgumentException validationError) { error = validationError.Message; return false; }

            startingDay = true;
            try
            {
                uint runSeed = randomSeed ?? context.Tuning.RandomSeed;
                BeforeDayBoardRestore();
                StartSessionCore(physics, 0, runSeed, grantAllStartingDevices, definition.Days[0].ProductionFeatures);
                dayPhysics.ResetDayPhysics();
                dayFlow = new DayFlowSystem(this, definition, ranges, retryRandom, runSeed);
                dayFlow.Start();
                error = null;
                return true;
            }
            finally { startingDay = false; }
        }

        private void ValidateRuleAttributes(System.Collections.Generic.IReadOnlyList<DayRuleDefinition> rules)
        {
            foreach (var rule in rules)
            {
                if (!rule.Attribute.HasValue) continue;
                var effect = rule.Attribute.Value;
                if (context.Content == null || !context.Content.Definition(effect.Attribute).Allows(effect.Operation))
                    throw new ArgumentException($"Day rule '{rule.RuleId}' is incompatible with this balance's {effect.Attribute} attribute.");
            }
        }

        public bool TryCompleteWakeUp(long version, out string error) =>
            TryCompleteDayPhase(DayTransitionReason.WakeUpCompleted, version, out error);

        public bool TryCompleteMorningPrep(string objectiveId, long version) =>
            !insidePhysicsStep && !startingDay && dayFlow != null && dayFlow.TryCompleteMorningPrep(objectiveId, version);

        public bool IsMorningPrepComplete(string objectiveId) => dayFlow != null && dayFlow.IsMorningPrepComplete(objectiveId);

        public bool TryCompleteDaySettlement(long version, out string error) =>
            TryCompleteDayPhase(DayTransitionReason.DaySettlementCompleted, version, out error);

        public bool TryCompleteRest(long version, out string error) =>
            TryCompleteDayPhase(DayTransitionReason.RestCompleted, version, out error);

        private bool TryCompleteDayPhase(DayTransitionReason reason, long version, out string error)
        {
            ThrowIfDisposed();
            if (insidePhysicsStep || startingDay || dayFlow == null)
            { error = "Day commands require a started campaign at a simulation step boundary."; return false; }
            return dayFlow.TryComplete(reason, version, out error);
        }

        public void PumpDayLifecycle()
        {
            ThrowIfDisposed();
            if (!insidePhysicsStep && !startingDay) dayFlow?.Pump();
        }

        internal void BeforeDayBoardRestore() => DayBoardResetting?.Invoke();
        internal void AfterDayBoardRestore() => DayBoardRestored?.Invoke();

        internal void PrepareDayProduction(DayDefinition day, int firstStage, int stageEnd, bool firstDay)
        {
            if (!firstDay) ResetRound(context.Physics, resetRandom: false);
            var campaign = context.Campaign;
            campaign.StageIndex = firstStage;
            campaign.StageStartIndex = firstStage;
            campaign.StageEndExclusive = stageEnd;
            campaign.StageTargetOverride = day.ProductionPlan.TargetScore ?? 0;
            campaign.DisabledFeatures = ProductionFeature.All & ~day.ProductionFeatures;
            campaign.StagesCleared = 0;
            campaign.TotalScore = 0;
            campaign.LastRewardCoins = campaign.LastCashoutCoins = 0;
            campaign.LastSkippedStages = campaign.ShopRefreshCount = 0;
            context.Campaign = campaign;
            context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity).Clear();
            var round = context.Round;
            round.Phase = (byte)RoundPhase.Build;
            round.TargetScore = CampaignStateUtility.TargetScore(context, firstStage);
            context.Round = round;
            FillBlood();
            if (AttributeRuntime.Enabled(context)) AttributeRuntime.Recompute(context, context.PlayerEntity);
        }
    }
}
