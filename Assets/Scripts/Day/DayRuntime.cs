using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    public enum DayCampaignStatus : byte { NotStarted, Active, Completed }
    public enum DayResult : byte { Pending, InProgress, Succeeded, Failed }
    public enum DayTransitionReason : byte
    {
        None, CampaignStarted, WakeUpCompleted, ProductionSucceeded, ProductionFailed, DaySettlementCompleted, RestCompleted, CampaignCompleted
    }

    public readonly struct DayProgressSnapshot
    {
        public DayCampaignStatus Status { get; }
        public string SequenceId { get; }
        public string DayId { get; }
        public int DayIndex { get; }
        public int DayCount { get; }
        public int CompletedDays { get; }
        public DayPhase Phase { get; }
        public DayPhase PreviousPhase { get; }
        public int Attempt { get; }
        public DayResult Result { get; }
        public bool SettlementCommitted { get; }
        public DayTransitionReason LastTransition { get; }
        /// <summary>Pass the version seen by the caller when completing an outer phase.</summary>
        public long Version { get; }
        public bool LifecyclePending { get; }
        public int MorningPrepRemaining { get; }
        public string ProductionPlanId { get; }
        public int SettledRounds { get; }
        public long ProductionTicks { get; }
        public uint RunSeed { get; }
        public uint AttemptStartRandomState { get; }
        public uint RandomState { get; }

        internal DayProgressSnapshot(DayRuntimeData state, DayDefinition definition, int count, uint randomState, bool pending)
        {
            Status = state.Status;
            SequenceId = state.SequenceId.ToString();
            DayId = state.DayId.ToString();
            DayIndex = state.DayIndex;
            DayCount = count;
            CompletedDays = state.CompletedDays;
            Phase = state.Phase;
            PreviousPhase = state.PreviousPhase;
            Attempt = state.Attempt;
            Result = state.Result;
            SettlementCommitted = state.SettlementCommitted;
            LastTransition = state.LastTransition;
            Version = state.Version;
            LifecyclePending = pending;
            MorningPrepRemaining = state.MorningPrepRemaining;
            ProductionPlanId = definition.ProductionPlan.PlanId;
            SettledRounds = state.SettledRounds;
            ProductionTicks = state.ProductionTicks;
            RunSeed = state.RunSeed;
            AttemptStartRandomState = state.AttemptStartRandomState;
            RandomState = randomState;
        }
    }

    public readonly struct DayLifecycleNotice
    {
        public DayProgressSnapshot Day { get; }
        public DayTrigger Trigger { get; }
        public long Occurrence { get; }
        public RoundPhase RoundPhase { get; }
        public int RoundId { get; }

        internal DayLifecycleNotice(DayProgressSnapshot day, DayTrigger trigger, long occurrence, RoundPhase roundPhase, int roundId)
        {
            Day = day;
            Trigger = trigger;
            Occurrence = occurrence;
            RoundPhase = roundPhase;
            RoundId = roundId;
        }
    }

    public readonly struct DayRetryRandomContext
    {
        public DayProgressSnapshot FailedAttempt { get; }
        public uint CheckpointState { get; }

        internal DayRetryRandomContext(DayProgressSnapshot failedAttempt, uint checkpointState)
        {
            FailedAttempt = failedAttempt;
            CheckpointState = checkpointState;
        }
    }

    public delegate uint DayRetryRandomPolicy(DayRetryRandomContext context);

    internal struct DayRuntimeData : IComponentData
    {
        internal FixedString128Bytes SequenceId, DayId;
        internal DayCampaignStatus Status;
        internal int DayIndex, CompletedDays, Attempt;
        internal DayPhase Phase, PreviousPhase;
        internal DayResult Result;
        internal bool SettlementCommitted;
        internal DayTransitionReason LastTransition;
        internal long Version, ProductionTicks;
        internal int SettledRounds, MorningPrepRemaining;
        internal uint RunSeed, AttemptStartRandomState;
    }

    internal struct MorningPrepProgress : IBufferElementData
    {
        internal FixedString128Bytes ObjectiveId;
        internal bool Completed;
    }
}
