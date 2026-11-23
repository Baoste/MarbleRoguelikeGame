namespace MarblesECS
{
    internal static class DayStateMachine
    {
        internal static bool CanTransition(DayRuntimeData state, DayTransitionReason reason)
        {
            if (reason == DayTransitionReason.CampaignStarted) return state.Status == DayCampaignStatus.NotStarted;
            if (state.Status != DayCampaignStatus.Active) return false;
            switch (reason)
            {
                case DayTransitionReason.WakeUpCompleted: return state.Phase == DayPhase.WakeUp;
                case DayTransitionReason.ProductionSucceeded:
                    return state.Phase == DayPhase.Production && state.Result == DayResult.Succeeded && !state.SettlementCommitted;
                case DayTransitionReason.ProductionFailed:
                    return state.Phase == DayPhase.Production && state.Result == DayResult.Failed;
                case DayTransitionReason.DaySettlementCompleted:
                    return state.Phase == DayPhase.DaySettlement && state.Result == DayResult.Succeeded && !state.SettlementCommitted;
                case DayTransitionReason.RestCompleted:
                    return state.Phase == DayPhase.Rest && state.SettlementCommitted;
                default: return false;
            }
        }

        internal static DayTrigger Entered(DayPhase phase) => phase switch
        {
            DayPhase.WakeUp => DayTrigger.WakeUpEntered,
            DayPhase.Production => DayTrigger.ProductionEntered,
            DayPhase.DaySettlement => DayTrigger.DaySettlementEntered,
            _ => DayTrigger.RestEntered
        };

        internal static DayTrigger Exited(DayPhase phase) => phase switch
        {
            DayPhase.WakeUp => DayTrigger.WakeUpExited,
            DayPhase.Production => DayTrigger.ProductionExited,
            DayPhase.DaySettlement => DayTrigger.DaySettlementExited,
            _ => DayTrigger.RestExited
        };
    }
}
