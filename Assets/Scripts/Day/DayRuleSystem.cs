using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal struct DayRuleData : IBufferElementData
    {
        internal FixedString128Bytes RuleId, SourceId;
        internal DayRuleScope Scope;
        internal uint Handle;
        internal long ExpiresAt;
    }

    internal sealed class DayRuleSystem : IDisposable
    {
        private readonly SimulationContext context;
        private readonly Dictionary<uint, IDayFeature> features = new Dictionary<uint, IDayFeature>();
        private uint nextHandle;
        private long nextExpiry = long.MaxValue;
        private DynamicBuffer<DayRuleData> Active => context.Manager.GetBuffer<DayRuleData>(context.RoundEntity);

        internal DayRuleSystem(SimulationContext context)
        {
            this.context = context;
            context.Manager.AddBuffer<DayRuleData>(context.RoundEntity);
        }

        internal void Install(IReadOnlyList<DayRuleDefinition> definitions, DayRuleScope scope, DayProgressSnapshot day)
        {
            foreach (var definition in definitions)
            {
                if (definition.Scope != scope || (definition.Phase.HasValue && definition.Phase != day.Phase)) continue;
                var entry = new DayRuleData
                {
                    RuleId = new FixedString128Bytes(definition.RuleId),
                    SourceId = new FixedString128Bytes(scope == DayRuleScope.Campaign ? day.SequenceId : day.DayId),
                    Scope = scope,
                    Handle = checked(++nextHandle),
                    ExpiresAt = scope == DayRuleScope.Temporary ? definition.DurationTicks : 0
                };
                if (definition.Attribute.HasValue)
                {
                    context.Manager.GetBuffer<AttributeModifierData>(context.PlayerEntity).Add(new AttributeModifierData
                    {
                        RuleHandle = entry.Handle, Effect = definition.Attribute.Value
                    });
                    AttributeRuntime.Recompute(context, context.PlayerEntity);
                }
                else if (!ActivateFeature(definition, entry, day)) continue;
                Active.Add(entry);
                if (entry.ExpiresAt != 0) nextExpiry = Math.Min(nextExpiry, entry.ExpiresAt);
            }
        }

        internal int RemoveSource(string sourceId, DayRuleScope scope) =>
            RemoveMatching(entry => entry.Scope == scope && entry.SourceId.ToString() == sourceId);

        internal void Expire(long elapsedTicks)
        {
            if (elapsedTicks < nextExpiry) return;
            RemoveMatching(entry => entry.ExpiresAt != 0 && entry.ExpiresAt <= elapsedTicks);
            nextExpiry = long.MaxValue;
            foreach (var entry in Active)
                if (entry.ExpiresAt != 0) nextExpiry = Math.Min(nextExpiry, entry.ExpiresAt);
        }

        internal DayRuleData[] Capture() => Active.AsNativeArray().ToArray();

        internal void Restore(DayRuleData[] checkpoint, IReadOnlyList<DayRuleDefinition> definitions, DayProgressSnapshot day)
        {
            foreach (var entry in checkpoint)
            {
                // Attribute entries were restored together with the player's modifier buffer.
                foreach (var definition in definitions)
                {
                    if (definition.RuleId != entry.RuleId.ToString()) continue;
                    if (definition.Attribute.HasValue || ActivateFeature(definition, entry, day)) Active.Add(entry);
                    break;
                }
            }
        }

        internal void ClearAttempt(DayProgressSnapshot day)
        {
            RemoveSource(day.DayId, DayRuleScope.Phase);
            RemoveSource(day.DayId, DayRuleScope.Temporary);
            RemoveSource(day.DayId, DayRuleScope.Day);
        }

        private int RemoveMatching(Predicate<DayRuleData> match)
        {
            var active = Active;
            var removed = new List<DayRuleData>();
            for (int i = active.Length - 1; i >= 0; i--)
            {
                var entry = active[i];
                if (!match(entry)) continue;
                removed.Add(entry);
                active.RemoveAt(i);
            }
            bool modifiersChanged = false;
            foreach (var entry in removed)
            {
                if (features.TryGetValue(entry.Handle, out var feature))
                {
                    features.Remove(entry.Handle);
                    ReleaseFeature(feature);
                    continue;
                }
                var modifiers = context.Manager.GetBuffer<AttributeModifierData>(context.PlayerEntity);
                for (int j = modifiers.Length - 1; j >= 0; j--)
                {
                    if (modifiers[j].RuleHandle != entry.Handle) continue;
                    modifiers.RemoveAt(j);
                    modifiersChanged = true;
                }
            }
            if (modifiersChanged) AttributeRuntime.Recompute(context, context.PlayerEntity);
            return removed.Count;
        }

        private bool ActivateFeature(DayRuleDefinition definition, DayRuleData entry, DayProgressSnapshot day)
        {
            IDayFeature feature = null;
            try
            {
                feature = definition.CreateFeature() ?? throw new InvalidOperationException("Feature factory returned null.");
                feature.Activate(day);
                features.Add(entry.Handle, feature);
                return true;
            }
            catch (Exception error)
            {
                if (feature != null) ReleaseFeature(feature);
                Debug.LogError($"Day rule '{definition.RuleId}' from '{entry.SourceId}' failed to activate: {error}");
                return false;
            }
        }

        private static void ReleaseFeature(IDayFeature feature)
        {
            try { feature.Deactivate(); }
            catch (Exception error) { Debug.LogException(error); }
            try { feature.Dispose(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        public void Dispose()
        {
            foreach (var feature in features.Values) ReleaseFeature(feature);
            features.Clear();
        }
    }
}
