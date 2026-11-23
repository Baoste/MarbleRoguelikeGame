using System;
using System.Collections.Generic;
using System.Text;
using MRes;
using UnityEngine;

namespace MarblesECS
{
    public static class GameDaysLoader
    {
        public static DayCampaignDefinition Load(GameBalance balance, DayBehaviorRegistry behaviors = null, TextAsset source = null)
        {
            source = source != null ? source : Resources.Load<TextAsset>("GameDays");
            if (source == null) throw new ArgumentException("Resources/GameDays.json is missing.");
            return Parse(source.text, balance, behaviors, source.name);
        }

        public static DayCampaignDefinition Parse(string json, GameBalance balance, DayBehaviorRegistry behaviors = null,
            string source = "GameDays.json")
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            behaviors = behaviors ?? new DayBehaviorRegistry();
            try
            {
                if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("JSON is empty.");
                if (json[0] == '\uFEFF') json = json.Substring(1);
                // Use the existing bounded reader so required fields, JSON types and duplicate keys are checked.
                var input = new MResFields(MResJson.Parse(json, "JSON"), "sequence");
                if (input.Int("schemaVersion") != 1) throw new ArgumentException("schemaVersion: expected 1.");
                string sequenceId = Id(input.String("sequenceId"), "sequenceId");
                var plans = ReadIndex(input, "productionPlans", "planId", (entry, id) =>
                {
                    var plan = new DayProductionPlan(id, StageId(entry, "firstStageId"), StageId(entry, "lastStageId"));
                    plan.Resolve(balance.Stages);
                    return plan;
                });
                var events = ReadIndex(input, "events", "eventId", (entry, id) => ReadEvent(entry, id, behaviors));
                var rules = ReadIndex(input, "rules", "ruleId", (entry, id) => ReadRule(entry, id, balance, behaviors));
                var campaignRules = ReadRefs(input, "campaignRuleIds", rules);
                CheckRuleScope(campaignRules, true, "campaignRuleIds");
                var days = ReadIndex(input, "days", "dayId", (entry, id) =>
                {
                    string path = entry.Context;
                    int order = entry.Int("order");
                    if (order <= 0) throw new ArgumentException($"{path}.order: expected a positive integer.");
                    var plan = Find(plans, entry.String("productionPlanId"), path + ".productionPlanId");
                    var dayRules = ReadRefs(entry, "ruleIds", rules);
                    CheckRuleScope(dayRules, false, path + ".ruleIds");
                    return new DayDefinition(id, order, plan, ReadRefs(entry, "eventIds", events), dayRules);
                }, required: true);
                var ordered = new List<DayDefinition>(days.Values);
                ordered.Sort((a, b) => a.Order.CompareTo(b.Order));
                for (int i = 1; i < ordered.Count; i++)
                    if (ordered[i - 1].Order == ordered[i].Order)
                        throw new ArgumentException($"Day '{ordered[i].DayId}'.order conflicts with '{ordered[i - 1].DayId}'.");
                return new DayCampaignDefinition(sequenceId, ordered.ToArray(), campaignRules);
            }
            catch (Exception error) when (error is ArgumentException || error is MResFormatException)
            {
                throw new ArgumentException($"{source}: {error.Message}", error);
            }
        }

        private static uint StageId(MResFields entry, string field)
        {
            double value = entry.Number(field);
            if (value <= 0 || value > uint.MaxValue || value != Math.Truncate(value)) throw entry.Error(field);
            return (uint)value;
        }

        private static DayEventDefinition ReadEvent(MResFields entry, string id, DayBehaviorRegistry behaviors)
        {
            string path = entry.Context;
            var trigger = ReadEnum<DayTrigger>(entry.String("trigger"), path + ".trigger");
            var policy = ReadEnum<DayEventPolicy>(entry.String("executionPolicy", nameof(DayEventPolicy.OncePerAttempt), optional: true),
                path + ".executionPolicy");
            Func<DayLifecycleNotice, bool> condition = null;
            if (entry.Has("condition"))
            {
                var spec = entry.Object("condition");
                condition = behaviors.ReadCondition(spec.String("type"), spec.String("parameters", optional: true), path + ".condition");
            }
            var input = entry.Array("actions");
            if (input.Count == 0) throw new ArgumentException($"{path}.actions: expected at least one action.");
            var actions = new DayAction[input.Count];
            for (int i = 0; i < actions.Length; i++)
            {
                var action = MResFields.From(input[i], $"{path}.actions[{i}]");
                actions[i] = behaviors.ReadAction(action.String("type"), action.String("parameters", optional: true), action.Context);
            }
            return new DayEventDefinition(id, trigger, entry.Int("priority", optional: true), policy, condition, actions);
        }

        private static DayRuleDefinition ReadRule(MResFields entry, string id, GameBalance balance, DayBehaviorRegistry behaviors)
        {
            string path = entry.Context;
            var scope = ReadEnum<DayRuleScope>(entry.String("scope"), path + ".scope");
            DayPhase? phase = scope == DayRuleScope.Phase ? ReadEnum<DayPhase>(entry.String("phase"), path + ".phase") : (DayPhase?)null;
            if (scope != DayRuleScope.Phase && entry.Has("phase"))
                throw new ArgumentException($"{path}.phase: only Phase rules accept this field.");
            long duration = 0;
            if (scope == DayRuleScope.Temporary)
            {
                double ticks = entry.Number("durationTicks");
                if (ticks <= 0 || ticks >= long.MaxValue || ticks != Math.Truncate(ticks)) throw entry.Error("durationTicks");
                duration = (long)ticks;
            }
            else if (entry.Has("durationTicks")) throw new ArgumentException($"{path}.durationTicks: only Temporary rules accept this field.");

            string type = entry.String("type");
            if (type != "Attribute")
                return new DayRuleDefinition(id, type, scope, phase, duration, null,
                    behaviors.ReadFeature(type, entry.String("parameters", optional: true), path));

            var attribute = ReadEnum<GameAttribute>(entry.String("attribute"), path + ".attribute");
            if (attribute == GameAttribute.Count || balance.Content == null)
                throw new ArgumentException($"{path}.attribute: expected an available content attribute.");
            var operation = ReadEnum<AttributeOperation>(entry.String("operation"), path + ".operation");
            if (!balance.Content.Definition(attribute).Allows(operation))
                throw new ArgumentException($"{path}.operation: {operation} is unavailable for {attribute}.");
            double value = entry.Number("value");
            if (operation == AttributeOperation.Multiply && value < 0)
                throw new ArgumentException($"{path}.value: invalid value for {operation}.");
            return new DayRuleDefinition(id, type, scope, phase, duration, new AttributeEffect
            {
                Attribute = attribute, Operation = operation, Value = value, Priority = entry.Int("priority", optional: true)
            }, null);
        }

        private static void CheckRuleScope(DayRuleDefinition[] rules, bool campaign, string path)
        {
            foreach (var rule in rules)
                if ((rule.Scope == DayRuleScope.Campaign) != campaign)
                    throw new ArgumentException($"{path}: rule '{rule.RuleId}' has incompatible scope {rule.Scope}.");
        }

        private static Dictionary<string, T> ReadIndex<T>(MResFields root, string field, string idField, Func<MResFields, string, T> read,
            bool required = false)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            var input = root.Array(field, optional: !required);
            for (int i = 0; i < input.Count; i++)
            {
                string path = $"{field}[{i}]";
                var item = MResFields.From(input[i], path);
                string id = Id(item.String(idField), path + "." + idField);
                path += $" ('{id}')";
                if (result.ContainsKey(id)) throw new ArgumentException($"{path}: duplicate ID '{id}'.");
                result.Add(id, read(new MResFields(item.Fields, path), id));
            }
            return result;
        }

        private static T[] ReadRefs<T>(MResFields entry, string field, Dictionary<string, T> index)
        {
            var input = entry.Array(field, optional: true);
            var result = new T[input.Count];
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < input.Count; i++)
            {
                string item = entry.Context + "." + field + "[" + i + "]";
                var id = input[i] as string;
                result[i] = Find(index, id, item);
                if (!used.Add(id)) throw new ArgumentException($"{item}: duplicate reference '{id}'.");
            }
            return result;
        }

        private static T Find<T>(Dictionary<string, T> index, string id, string path)
        {
            if (index.TryGetValue(Id(id, path), out var result)) return result;
            throw new ArgumentException($"{path}: unknown reference '{id}'.");
        }

        private static string Id(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() != value || value.IndexOf('\0') >= 0 ||
                Encoding.UTF8.GetByteCount(value) > 120)
                throw new ArgumentException($"{path}: expected a stable ID of 1–120 UTF-8 bytes without surrounding whitespace.");
            return value;
        }

        private static T ReadEnum<T>(string value, string path) where T : struct, Enum
        {
            if (Enum.TryParse(value, out T result) && Enum.GetName(typeof(T), result) == value) return result;
            throw new ArgumentException($"{path}: unknown {typeof(T).Name} '{value}'.");
        }
    }
}
