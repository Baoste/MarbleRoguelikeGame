using System;
using Unity.Collections;

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
        public bool LoadoutConfirmed => dayFlow == null || dayFlow.LoadoutConfirmed;
        public bool HasProductionFeature(ProductionFeature feature) => context.HasProductionFeature(feature);
        internal bool HasDayFlow => dayFlow != null;
        public bool ProductionCommandsAvailable => !startingDay && (dayFlow == null || dayFlow.AllowsProduction);
        internal bool CanSimulateProduction => ProductionCommandsAvailable && context.IsRunning;

        public bool TryStartDayCampaign(DayCampaignDefinition definition, IMarblePhysics physics, out string error,
            uint? randomSeed = null, DayRetryRandomPolicy retryRandom = null)
        {
            ThrowIfDisposed();
            if (dayFlow != null || startingDay || insidePhysicsStep)
            { error = "A Day campaign can only start once at a simulation step boundary."; return false; }
            if (context.Balance == null)
            { error = "Day campaigns require the existing GameBalance production session."; return false; }
            if (definition == null || !definition.IsConfigured)
            { error = "The Day sequence is unconfigured. Add at least one Day before starting."; return false; }
            if (definition.Days[0].DailyLoadout != null && context.Content == null)
            { error = "Daily loadouts require GameContent for their device effects."; return false; }
            if (!(physics is IMarbleDayPhysics dayPhysics) || (started && !ReferenceEquals(physics, context.Physics)))
            { error = "Use the current physics adapter with Day checkpoint support."; return false; }

            try
            {
                ValidateRuleAttributes(definition.CampaignRules);
                foreach (var day in definition.Days)
                {
                    if (day.InitialBiomassScrap < 0 || day.InitialBiomassScrap > CampaignWalletSystem.MaxBalance)
                        throw new ArgumentException($"Day '{day.DayId}' initialBiomassScrap is outside the wallet range.");
                    if (day.Production.Supply == DeviceSupply.SelectedTypes && day.DailyLoadout == null)
                        throw new ArgumentException($"Day '{day.DayId}' needs a loadout for selected device types.");
                    if (day.SkipMorningPrep && day.DailyLoadout is DailyLoadoutDefinition loadout &&
                        loadout.MinLoadoutTypeCount > (loadout.SelectionMode == LoadoutSelectionMode.AutoSelectAvailable
                            ? loadout.DefaultPegIds.Count : loadout.RequiredPegIds.Count))
                        throw new ArgumentException($"Day '{day.DayId}' needs enough preselected types to skip Morning Prep.");
                    if (day.TargetScore > context.Tuning.MaxRoundScore)
                        throw new ArgumentException($"Day '{day.DayId}' targetScore exceeds this session's MaxRoundScore.");
                    if (day.BloodProgression?.Stages.Count > 0 && context.Content == null)
                        throw new ArgumentException($"Day '{day.DayId}' blood progression requires GameContent.");
                    ValidateRuleAttributes(day.Rules);
                    if (day.DailyLoadout != null)
                        foreach (uint id in day.DailyLoadout.UnlockedPegIds)
                        {
                            var device = CampaignStateUtility.Device(context, id);
                            if (device == null || device.PlacementRadius <= 0)
                                throw new ArgumentException($"Day '{day.DayId}' cannot install device {id} with this balance.");
                        }
                    if (day.BoardLayout == null) continue;
                    if (!(physics is IMarbleBoardLayoutPhysics boardPhysics))
                        throw new ArgumentException($"Day '{day.DayId}' requires board layout physics support.");
                    boardPhysics.ValidateBoardLayout(day.BoardLayout);
                }
            }
            catch (ArgumentException validationError) { error = validationError.Message; return false; }

            startingDay = true;
            try
            {
                uint runSeed = randomSeed ?? context.Tuning.RandomSeed;
                BeforeDayBoardRestore();
                var firstDay = definition.Days[0];
                StartSessionCore(physics, runSeed, firstDay.ProductionFeatures, firstDay.Production, definition.StartingCompanyScrip);
                dayPhysics.ResetDayPhysics();
                dayFlow = new DayFlowSystem(this, definition, retryRandom, runSeed);
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

        internal void ClearDailyProduction()
        {
            BeforeDayBoardRestore();
            using (var devices = context.DeviceQuery.ToEntityArray(Allocator.Temp))
                if (devices.Length != 0)
                {
                    context.Manager.DestroyEntity(devices);
                    var campaign = context.Campaign;
                    campaign.LayoutRevision++;
                    context.Campaign = campaign;
                    DeviceDurabilitySystem.Refresh(context);
                }
            context.DeviceScripts.Clear();
        }

        internal void PrepareDayProduction(DayDefinition day, bool firstDay)
        {
            if (!firstDay) ResetRound(context.Physics, resetRandom: false);
            if (context.Physics is IMarbleBoardLayoutPhysics boardPhysics) boardPhysics.ApplyBoardLayout(day.BoardLayout);
            context.BloodProgression = day.BloodProgression;
            var campaign = context.Campaign;
            campaign.Production = day.Production;
            campaign.RepeatRounds = false;
            campaign.DisabledFeatures = ProductionFeature.All & ~day.ProductionFeatures;
            campaign.TotalScore = 0;
            campaign.LastCompanyScripReward = 0;
            campaign.ShopRefreshCount = 0;
            context.Campaign = campaign;
            context.Manager.GetBuffer<ShopOfferData>(context.RoundEntity).Clear();
            var round = context.Round;
            round.Phase = (byte)RoundPhase.Build;
            round.TargetScore = day.TargetScore;
            context.Round = round;
            FillBlood();
            CampaignWalletSystem.Set(context, GameCurrency.BiomassScrap, day.InitialBiomassScrap);
            if (day.DailyLoadout != null) DailyLoadoutSystem.StartDay(context, day.DailyLoadout);
            if (AttributeRuntime.Enabled(context)) AttributeRuntime.Recompute(context, context.PlayerEntity);
        }
    }
}
