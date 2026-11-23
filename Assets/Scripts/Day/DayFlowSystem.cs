using System;
using Unity.Collections;
using UnityEngine;

namespace MarblesECS
{
    // Explicitly scheduled like the other Simulation systems. Mutable flow authority lives on RoundEntity.
    internal sealed class DayFlowSystem : IDisposable
    {
        private readonly MarbleSimulation simulation;
        private readonly SimulationContext context;
        private readonly DayCampaignDefinition definition;
        private readonly (int first, int end)[] stageRanges;
        private readonly DayRetryRandomPolicy retryRandom;
        private readonly DayRuleSystem rules;
        private readonly DayEventSystem events;
        private DayCheckpoint checkpoint;
        private DayTransitionReason transition, outcome;
        private long occurrence;
        private bool processing, faulted;

        private DayRuntimeData State
        {
            get => context.Manager.GetComponentData<DayRuntimeData>(context.RoundEntity);
            set => context.Manager.SetComponentData(context.RoundEntity, value);
        }

        internal DayDefinition Current => definition.Days[State.DayIndex];
        private bool Busy => processing || faulted || events.IsBusy ||
            transition != DayTransitionReason.None || outcome != DayTransitionReason.None;
        internal bool AllowsProduction
        {
            get
            {
                if (Busy) return false;
                var state = State;
                return state.Phase == DayPhase.Production && state.Result == DayResult.InProgress;
            }
        }
        internal DayProgressSnapshot Snapshot => new DayProgressSnapshot(State, Current, definition.Days.Count, context.Round.RandomState, Busy);

        internal DayFlowSystem(MarbleSimulation simulation, DayCampaignDefinition definition,
            (int first, int end)[] stageRanges, DayRetryRandomPolicy retryRandom, uint runSeed)
        {
            this.simulation = simulation;
            context = simulation.Context;
            this.definition = definition;
            this.stageRanges = stageRanges;
            this.retryRandom = retryRandom;
            context.Manager.AddComponentData(context.RoundEntity,
                new DayRuntimeData { SequenceId = new FixedString128Bytes(definition.SequenceId), RunSeed = runSeed });
            context.Manager.AddBuffer<MorningPrepProgress>(context.RoundEntity);
            rules = new DayRuleSystem(context);
            events = new DayEventSystem(context, rules);
        }

        internal void Start()
        {
            transition = DayTransitionReason.CampaignStarted;
            Pump();
        }

        internal bool TryComplete(DayTransitionReason reason, long version, out string error)
        {
            var state = State;
            if (state.Version != version) { error = "The Day phase version is stale."; return false; }
            if (Busy) { error = "The Day lifecycle is still processing."; return false; }
            if (reason == DayTransitionReason.WakeUpCompleted && state.Phase == DayPhase.WakeUp && state.MorningPrepRemaining != 0)
            { error = "Finish Morning Prep before using the marble machine."; return false; }
            if (!DayStateMachine.CanTransition(state, reason))
            {
                error = $"Day '{state.DayId}' attempt {state.Attempt}: {reason} is invalid in {state.Status}/{state.Phase}.";
                return false;
            }
            QueueTransition(reason);
            Pump();
            error = null;
            return true;
        }

        internal bool TryCompleteMorningPrep(string objectiveId, long version)
        {
            var state = State;
            if (state.Status != DayCampaignStatus.Active || state.Phase != DayPhase.WakeUp ||
                state.Version != version || state.MorningPrepRemaining == 0 || Busy) return false;
            var objectives = context.Manager.GetBuffer<MorningPrepProgress>(context.RoundEntity);
            for (int i = 0; i < Current.MorningPrep.Count; i++)
            {
                if (Current.MorningPrep[i].ObjectiveId != objectiveId) continue;
                var objective = objectives[i];
                if (objective.Completed) return false;
                objective.Completed = true;
                objectives[i] = objective;
                state.MorningPrepRemaining--;
                State = state;
                return true;
            }
            return false;
        }

        internal bool IsMorningPrepComplete(string objectiveId)
        {
            var objectives = context.Manager.GetBuffer<MorningPrepProgress>(context.RoundEntity);
            foreach (var item in objectives)
                if (item.Completed && item.ObjectiveId.ToString() == objectiveId) return true;
            return false;
        }

        internal void ObserveRound(RoundData before, RoundData after, bool stepped, bool cashout = false)
        {
            processing = true;
            try { RecordRound(before, after, stepped, cashout); }
            catch { faulted = true; throw; }
            finally { processing = false; }
            Pump();
        }

        private void RecordRound(RoundData before, RoundData after, bool stepped, bool cashout)
        {
            var state = State;
            if (state.Phase != DayPhase.Production || state.Result != DayResult.InProgress) return;
            if (stepped)
            {
                state.ProductionTicks = checked(state.ProductionTicks + 1);
                State = state;
                rules.Expire(state.ProductionTicks);
            }

            var phase = (RoundPhase)after.Phase;
            if (before.Phase == (byte)RoundPhase.Playing && (cashout ||
                phase == RoundPhase.Draining || (stepped && after.SettlementComplete)))
                Publish(DayTrigger.DrainingEntered, RoundPhase.Draining);

            if (after.SettlementComplete && (!before.SettlementComplete || before.RoundId != after.RoundId))
            {
                state.SettledRounds++;
                State = state;
                Publish(DayTrigger.RoundSettled);
                if (phase == RoundPhase.Shop) Publish(DayTrigger.ShopEntered);
                else if (phase == RoundPhase.Build) Publish(DayTrigger.BuildEntered);
                else ReportProductionResult(phase);
            }
            else if (before.Phase != after.Phase)
            {
                if (phase == RoundPhase.Build) Publish(DayTrigger.BuildEntered);
                if (phase == RoundPhase.Playing) Publish(DayTrigger.PlayingEntered);
            }
        }

        private void ReportProductionResult(RoundPhase phase)
        {
            if (phase != RoundPhase.Won && phase != RoundPhase.Lost)
                throw new InvalidOperationException($"Unexpected terminal production phase {phase}.");
            var state = State;
            bool succeeded = phase == RoundPhase.Won;
            state.Result = succeeded ? DayResult.Succeeded : DayResult.Failed;
            State = state;
            outcome = succeeded ? DayTransitionReason.ProductionSucceeded : DayTransitionReason.ProductionFailed;
            Publish(succeeded ? DayTrigger.ProductionSucceeded : DayTrigger.ProductionFailed);
        }

        internal void Pump()
        {
            if (processing || faulted ||
                (!events.IsBusy && transition == DayTransitionReason.None && outcome == DayTransitionReason.None)) return;
            processing = true;
            try
            {
                while (true)
                {
                    if (!events.Drain()) return;
                    if (transition != DayTransitionReason.None)
                    {
                        var reason = transition;
                        transition = DayTransitionReason.None;
                        Commit(reason);
                    }
                    else if (outcome != DayTransitionReason.None)
                    {
                        var reason = outcome;
                        outcome = DayTransitionReason.None;
                        QueueTransition(reason);
                    }
                    else return;
                }
            }
            catch { faulted = true; throw; }
            finally { processing = false; }
        }

        private void QueueTransition(DayTransitionReason reason)
        {
            transition = reason;
            Publish(DayStateMachine.Exited(State.Phase));
            if (reason == DayTransitionReason.RestCompleted) Publish(DayTrigger.DayCompleted);
        }

        private void Commit(DayTransitionReason reason)
        {
            var state = State;
            if (!DayStateMachine.CanTransition(state, reason))
                throw new InvalidOperationException($"Invalid Day transition {reason} from {state.Status}/{state.Phase}.");

            state.PreviousPhase = state.Phase;
            state.LastTransition = reason;
            state.Version = checked(state.Version + 1);
            if (reason != DayTransitionReason.CampaignStarted)
            {
                rules.RemoveSource(Current.DayId, DayRuleScope.Phase);
                if (state.Phase == DayPhase.Production) rules.RemoveSource(Current.DayId, DayRuleScope.Temporary);
            }

            switch (reason)
            {
                case DayTransitionReason.CampaignStarted:
                    state.Status = DayCampaignStatus.Active;
                    EnterNewDay(state, true);
                    break;
                case DayTransitionReason.WakeUpCompleted:
                    state.Phase = DayPhase.Production;
                    state.Result = DayResult.InProgress;
                    State = state;
                    rules.Install(Current.Rules, DayRuleScope.Temporary, Snapshot);
                    EnterPhase();
                    Publish(DayTrigger.BuildEntered);
                    break;
                case DayTransitionReason.ProductionSucceeded:
                    state.Phase = DayPhase.DaySettlement;
                    State = state;
                    EnterPhase();
                    break;
                case DayTransitionReason.ProductionFailed:
                    RetryCurrentDay(state);
                    break;
                case DayTransitionReason.DaySettlementCompleted:
                    state.SettlementCommitted = true;
                    state.Phase = DayPhase.Rest;
                    State = state;
                    EnterPhase();
                    break;
                case DayTransitionReason.RestCompleted:
                    rules.ClearAttempt(Snapshot);
                    state.CompletedDays++;
                    if (state.CompletedDays == definition.Days.Count)
                    {
                        rules.RemoveSource(definition.SequenceId, DayRuleScope.Campaign);
                        events.ClearAttempt();
                        checkpoint = null;
                        state.Status = DayCampaignStatus.Completed;
                        state.LastTransition = DayTransitionReason.CampaignCompleted;
                        State = state;
                    }
                    else
                    {
                        state.DayIndex++;
                        EnterNewDay(state, false);
                    }
                    break;
            }
        }

        private void EnterNewDay(DayRuntimeData state, bool first)
        {
            var day = definition.Days[state.DayIndex];
            var range = stageRanges[state.DayIndex];
            if (!first) simulation.BeforeDayBoardRestore();
            simulation.PrepareDayProduction(day, range.first, range.end, first);
            state.DayId = new FixedString128Bytes(day.DayId);
            state.Attempt = 1;
            InitializeWakeUp(ref state, day);
            State = state;
            events.SetDay(day);
            if (first) rules.Install(definition.CampaignRules, DayRuleScope.Campaign, Snapshot);
            checkpoint = new DayCheckpoint(context, rules.Capture());
            simulation.AfterDayBoardRestore();
            rules.Install(day.Rules, DayRuleScope.Day, Snapshot);
            EnterPhase();
        }

        private void RetryCurrentDay(DayRuntimeData state)
        {
            uint random = checkpoint.RandomState;
            try
            {
                if (retryRandom != null) random = retryRandom(new DayRetryRandomContext(Snapshot, random));
            }
            catch (Exception error)
            {
                Debug.LogError($"Day '{state.DayId}' retry random policy failed. Restoring checkpoint RNG: {error}");
            }
            rules.ClearAttempt(Snapshot);
            rules.RemoveSource(definition.SequenceId, DayRuleScope.Campaign);
            events.ClearAttempt();
            simulation.BeforeDayBoardRestore();
            checkpoint.Restore(context, random);
            state.Attempt = checked(state.Attempt + 1);
            InitializeWakeUp(ref state, Current);
            State = state;
            rules.Restore(checkpoint.Rules, definition.CampaignRules, Snapshot);
            simulation.AfterDayBoardRestore();
            rules.Install(Current.Rules, DayRuleScope.Day, Snapshot);
            rules.Install(Current.Rules, DayRuleScope.Phase, Snapshot);
            Publish(DayTrigger.DayRetried);
            Publish(DayTrigger.WakeUpEntered);
        }

        private void InitializeWakeUp(ref DayRuntimeData state, DayDefinition day)
        {
            state.Phase = DayPhase.WakeUp;
            state.Result = DayResult.Pending;
            state.SettlementCommitted = false;
            state.SettledRounds = 0;
            state.ProductionTicks = 0;
            state.AttemptStartRandomState = context.Round.RandomState;
            var objectives = context.Manager.GetBuffer<MorningPrepProgress>(context.RoundEntity);
            objectives.Clear();
            foreach (var objective in day.MorningPrep)
                objectives.Add(new MorningPrepProgress { ObjectiveId = new FixedString128Bytes(objective.ObjectiveId) });
            state.MorningPrepRemaining = objectives.Length;
        }

        private void EnterPhase()
        {
            rules.Install(Current.Rules, DayRuleScope.Phase, Snapshot);
            Publish(DayStateMachine.Entered(State.Phase));
        }

        private void Publish(DayTrigger trigger, RoundPhase? phase = null)
        {
            var round = context.Round;
            events.Publish(new DayLifecycleNotice(Snapshot, trigger, checked(++occurrence), phase ?? (RoundPhase)round.Phase, round.RoundId));
        }

        public void Dispose()
        {
            events.Dispose();
            rules.Dispose();
            checkpoint = null;
        }
    }
}
