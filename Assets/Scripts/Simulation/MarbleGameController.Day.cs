using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        [Header("Daily campaign")]
        public bool StartWithDayCampaign;
        private Func<GameBalance, DayCampaignDefinition, DayCampaignDefinition> configureDayCampaign;
        public DayProgressSnapshot Day => IsReady ? simulation.Day : default;
        public DayDefinition CurrentDayDefinition => IsReady ? simulation.CurrentDayDefinition : null;
        public bool ProductionCommandsAvailable => IsReady && simulation.ProductionCommandsAvailable;
        public bool HasProductionFeature(ProductionFeature feature) => IsReady && simulation.HasProductionFeature(feature);

        public void ConfigureDayCampaign(Func<GameBalance, DayCampaignDefinition, DayCampaignDefinition> configure)
        {
            if (simulation != null) throw new InvalidOperationException("Configure the Day campaign before the controller starts.");
            configureDayCampaign = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        public bool TryStartConfiguredDayCampaign(out string error, DayBehaviorRegistry behaviors = null,
            DayRetryRandomPolicy retryRandom = null)
        {
            if (!IsReady || !EnableCampaign)
            { error = "Start a campaign-enabled controller before starting the Day sequence."; return false; }
            DayCampaignDefinition definition;
            try
            {
                definition = GameDaysLoader.Load(Balance, behaviors);
                if (configureDayCampaign != null) definition = configureDayCampaign(Balance, definition);
                configureDayCampaign = null;
            }
            catch (ArgumentException configurationError) { error = configurationError.Message; return false; }
            return TryStartDayCampaign(definition, out error, retryRandom: retryRandom);
        }

        public bool TryStartDayCampaign(DayCampaignDefinition definition, out string error, uint? randomSeed = null,
            DayRetryRandomPolicy retryRandom = null)
        {
            if (!IsReady || !EnableCampaign || RequiresRestart)
            { error = "A ready campaign-enabled controller is required."; return false; }
            uint seed = randomSeed ?? (RefreshRandomSeedOnNewRun ? CreateRunRandomSeed() : Balance.Marble.RandomSeed);
            if (!simulation.TryStartDayCampaign(definition, bridge, out error, seed, retryRandom))
                return false;
            LastRunRandomSeed = seed;
            PublishBloodIfChanged();
            return true;
        }

        public bool TryCompleteWakeUp(long version, out string error)
        {
            if (!DayCommandsReady(out error)) return false;
            return simulation.TryCompleteWakeUp(version, out error);
        }

        internal bool TryCompleteMorningPrep(string objectiveId, long version) =>
            IsReady && !RequiresRestart && simulation.TryCompleteMorningPrep(objectiveId, version);

        internal bool IsMorningPrepComplete(string objectiveId) => IsReady && simulation.IsMorningPrepComplete(objectiveId);

        public bool TryCompleteDaySettlement(long version, out string error)
        {
            if (!DayCommandsReady(out error)) return false;
            return simulation.TryCompleteDaySettlement(version, out error);
        }

        public bool TryCompleteRest(long version, out string error)
        {
            if (!DayCommandsReady(out error)) return false;
            bool accepted = simulation.TryCompleteRest(version, out error);
            if (accepted) PublishBloodIfChanged();
            return accepted;
        }

        private bool DayCommandsReady(out string error)
        {
            error = IsReady && !RequiresRestart ? null : "The Day controller is unavailable.";
            return error == null;
        }

        private void BeforeDayBoardRestore()
        {
            accumulator = 0;
            externalFireHeld = submittedFireHeld = false;
        }

        private void AfterDayBoardRestore()
        {
            SyncLauncherTransform();
        }
    }
}
