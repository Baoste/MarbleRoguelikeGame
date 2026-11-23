using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        public DayProgressSnapshot Day => IsReady ? simulation.Day : default;
        public bool ProductionCommandsAvailable => IsReady && simulation.ProductionCommandsAvailable;
        internal event Action DayBoardResetting;
        internal event Action DayBoardRestored;

        public bool TryStartConfiguredDayCampaign(out string error, DayBehaviorRegistry behaviors = null,
            DayRetryRandomPolicy retryRandom = null)
        {
            if (!IsReady || !EnableCampaign)
            { error = "Start a campaign-enabled controller before starting the Day sequence."; return false; }
            DayCampaignDefinition definition;
            try { definition = GameDaysLoader.Load(Balance, behaviors); }
            catch (ArgumentException configurationError) { error = configurationError.Message; return false; }
            return TryStartDayCampaign(definition, out error, retryRandom: retryRandom);
        }

        public bool TryStartDayCampaign(DayCampaignDefinition definition, out string error, uint? randomSeed = null,
            DayRetryRandomPolicy retryRandom = null)
        {
            if (!IsReady || !EnableCampaign || RequiresRestart)
            { error = "A ready campaign-enabled controller is required."; return false; }
            uint seed = randomSeed ?? (RefreshRandomSeedOnNewRun ? CreateRunRandomSeed() : Balance.Marble.RandomSeed);
            if (!simulation.TryStartDayCampaign(definition, bridge, out error, seed, retryRandom, EnableRandomStartingDevices))
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
            DayBoardResetting?.Invoke();
            var text = WorldTextEffectPool.Instance;
            if (text != null && (text.gameObject.scene == originalScene || text.gameObject.scene == localScene)) text.ClearActive();
        }

        private void AfterDayBoardRestore()
        {
            SyncLauncherTransform();
            DayBoardRestored?.Invoke();
            bridge.RefreshDeviceAvailability();
            Physics.SyncTransforms();
        }
    }
}
