using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal enum DayEventStatus : byte { Running, Completed, Failed }
    internal struct DayEventRecord : IBufferElementData
    {
        internal FixedString128Bytes EventId;
        internal long Occurrence;
        internal int NextAction;
        internal DayEventStatus Status;
    }

    /// <summary>Access to the notification and scoped rules for the lifetime of one action.</summary>
    public sealed class DayActionContext
    {
        public DayLifecycleNotice Notice { get; }
        private DayRuleSystem rules;
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;

        internal DayActionContext(DayLifecycleNotice notice, DayRuleSystem rules)
        {
            Notice = notice;
            this.rules = rules;
        }

        public int RemoveRules(string sourceId, DayRuleScope scope)
        {
            if (Thread.CurrentThread.ManagedThreadId != thread)
                throw new InvalidOperationException("Day actions must access gameplay on the simulation thread.");
            if (rules == null) throw new InvalidOperationException("This Day action is no longer active.");
            return rules.RemoveSource(sourceId, scope);
        }

        internal void Close() => rules = null;
    }

    internal sealed class DayEventSystem : IDisposable
    {
        private readonly SimulationContext context;
        private readonly DayRuleSystem rules;
        private readonly Dictionary<DayTrigger, List<DayEventDefinition>> byTrigger = new Dictionary<DayTrigger, List<DayEventDefinition>>();
        private readonly Queue<(DayLifecycleNotice notice, DayEventDefinition definition)> queue =
            new Queue<(DayLifecycleNotice notice, DayEventDefinition definition)>();
        private long lastOccurrence;
        private int recordIndex;
        private bool eventStarted;
        private Task task;
        private DayActionContext actionContext;
        private CancellationTokenSource cancellation;

        internal bool IsBusy => queue.Count != 0;
        private DynamicBuffer<DayEventRecord> Records => context.Manager.GetBuffer<DayEventRecord>(context.RoundEntity);

        internal DayEventSystem(SimulationContext context, DayRuleSystem rules)
        {
            this.context = context;
            this.rules = rules;
            context.Manager.AddBuffer<DayEventRecord>(context.RoundEntity);
        }

        internal void SetDay(DayDefinition day)
        {
            ClearAttempt();
            byTrigger.Clear();
            foreach (var definition in day.Events)
            {
                if (!byTrigger.TryGetValue(definition.Trigger, out var group))
                    byTrigger.Add(definition.Trigger, group = new List<DayEventDefinition>());
                group.Add(definition);
            }
            foreach (var group in byTrigger.Values)
                group.Sort((a, b) => a.Priority == b.Priority ? string.CompareOrdinal(a.EventId, b.EventId) :
                    b.Priority.CompareTo(a.Priority));
        }

        internal void ClearAttempt()
        {
            CancelAction();
            queue.Clear();
            eventStarted = false;
            Records.Clear();
        }

        internal void Publish(DayLifecycleNotice notice)
        {
            if (notice.Occurrence <= lastOccurrence) return;
            lastOccurrence = notice.Occurrence;
            if (byTrigger.TryGetValue(notice.Trigger, out var group))
                foreach (var definition in group) queue.Enqueue((notice, definition));
        }

        internal bool Drain()
        {
            while (queue.Count > 0)
            {
                var (notice, definition) = queue.Peek();
                try
                {
                    if (!eventStarted && !TryStart(notice, definition)) { queue.Dequeue(); continue; }
                    var record = Records[recordIndex];
                    if (record.NextAction == definition.Actions.Count)
                    {
                        Finish(DayEventStatus.Completed);
                        continue;
                    }
                    if (task == null)
                    {
                        cancellation = new CancellationTokenSource();
                        actionContext = new DayActionContext(notice, rules);
                        task = definition.Actions[record.NextAction](actionContext, cancellation.Token) ??
                            throw new InvalidOperationException("Action returned no completion task.");
                    }
                    if (!task.IsCompleted) return false;
                    task.GetAwaiter().GetResult();
                    CloseAction();
                    record.NextAction++;
                    var buffer = Records;
                    buffer[recordIndex] = record;
                }
                catch (Exception error)
                {
                    CloseAction();
                    if (eventStarted) Finish(DayEventStatus.Failed);
                    else queue.Dequeue();
                    Debug.LogError($"Day '{notice.Day.DayId}' event '{definition.EventId}' ({notice.Trigger}) failed: {error}");
                }
            }
            return true;
        }

        private bool TryStart(DayLifecycleNotice notice, DayEventDefinition definition)
        {
            var id = new FixedString128Bytes(definition.EventId);
            var buffer = Records;
            int index = 0;
            for (; index < buffer.Length; index++)
            {
                var entry = buffer[index];
                if (!entry.EventId.Equals(id)) continue;
                if (entry.Occurrence == notice.Occurrence || definition.Policy == DayEventPolicy.OncePerAttempt) return false;
                break;
            }
            // Mark the occurrence before invoking authored code, including a condition that might throw.
            var record = new DayEventRecord { EventId = id, Occurrence = notice.Occurrence, Status = DayEventStatus.Running };
            if (index == buffer.Length) buffer.Add(record);
            else buffer[index] = record;
            recordIndex = index;
            eventStarted = true;
            if (definition.Condition?.Invoke(notice) != false) return true;
            // A skipped condition does not consume OncePerAttempt.
            Records.RemoveAt(recordIndex);
            eventStarted = false;
            return false;
        }

        private void Finish(DayEventStatus status)
        {
            var buffer = Records;
            var record = buffer[recordIndex];
            record.Status = status;
            buffer[recordIndex] = record;
            eventStarted = false;
            queue.Dequeue();
        }

        private void CloseAction()
        {
            actionContext?.Close();
            actionContext = null;
            cancellation?.Dispose();
            cancellation = null;
            task = null;
        }

        private void CancelAction()
        {
            actionContext?.Close();
            try { cancellation?.Cancel(); }
            catch (Exception error) { Debug.LogException(error); }
            if (task != null)
                _ = task.ContinueWith(failed => { _ = failed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            CloseAction();
        }

        public void Dispose()
        {
            CancelAction();
            queue.Clear();
            byTrigger.Clear();
        }
    }
}
