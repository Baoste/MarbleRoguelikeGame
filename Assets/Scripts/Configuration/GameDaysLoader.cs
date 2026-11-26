using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                var input = new ConfigFields(ConfigJson.Parse(json, "JSON"), "sequence");
                int schemaVersion = input.Int("schemaVersion");
                bool dailyLoadouts = schemaVersion == DayCampaignDefinition.CurrentSchemaVersion;
                if (!dailyLoadouts && schemaVersion != DayCampaignDefinition.LegacySchemaVersion)
                    throw new ArgumentException($"schemaVersion: expected {DayCampaignDefinition.CurrentSchemaVersion} or {DayCampaignDefinition.LegacySchemaVersion}.");
                long startingScrip = dailyLoadouts && input.Has("startingCompanyScrip")
                    ? ReadInteger(input, "startingCompanyScrip", 0, CampaignWalletSystem.MaxBalance) : 0;
                string sequenceId = Id(input.String("sequenceId"), "sequenceId");
                var defaultFeatures = ReadProductionFeatures(input, "defaultProductionFeatures");
                var defaultBlood = input.Has("defaultBloodProgression")
                    ? ReadBloodProgression(input.Object("defaultBloodProgression"), balance, null) : null;
                var devices = balance.Devices.ToDictionary(device => device.Id);
                var events = ReadIndex(input, "events", "eventId", (entry, id) => ReadEvent(entry, id, behaviors));
                var rules = ReadIndex(input, "rules", "ruleId", (entry, id) => ReadRule(entry, id, balance, behaviors));
                var campaignRules = ReadRefs(input, "campaignRuleIds", rules);
                CheckRuleScope(campaignRules, true, "campaignRuleIds");
                var days = ReadIndex(input, "days", "dayId", (entry, id) =>
                {
                    string path = entry.Context;
                    int order = entry.Int("order");
                    if (order <= 0) throw new ArgumentException($"{path}.order: expected a positive integer.");
                    long targetScore = ReadInteger(entry, "targetScore", 1, balance.Marble.MaxRoundScore);
                    long rewardCoins = dailyLoadouts ? 0 : ReadInteger(entry, "rewardCoins", 0, CampaignStateUtility.MaxCoins);
                    var loadout = dailyLoadouts ? ReadDailyLoadout(entry, devices) : null;
                    var blood = entry.Has("bloodProgression")
                        ? ReadBloodProgression(entry.Object("bloodProgression"), balance, defaultBlood) : defaultBlood;
                    var dayRules = ReadRefs(entry, "ruleIds", rules);
                    CheckRuleScope(dayRules, false, path + ".ruleIds");
                    string boardLayoutFile = entry.Has("boardLayout") ? entry.String("boardLayout") : null;
                    BoardLayoutDefinition boardLayout = null;
                    if (boardLayoutFile != null)
                    {
                        try { boardLayout = BoardLayoutLoader.Load(boardLayoutFile); }
                        catch (ArgumentException error) { throw new ArgumentException($"{path}.boardLayout: {error.Message}", error); }
                    }
                    var features = defaultFeatures | ReadProductionFeatures(entry, "productionFeatures");
                    if (loadout != null)
                    {
                        features = (features | ProductionFeature.DevicePlacement) & ~ProductionFeature.Shop;
                        if (loadout.Shop != null) features |= ProductionFeature.Shop;
                    }
                    return new DayDefinition(id, order, targetScore, rewardCoins, features, boardLayoutFile,
                        boardLayout, ReadMorningPrep(entry), ReadRefs(entry, "eventIds", events), dayRules, loadout, blood);
                }, required: true);
                var ordered = new List<DayDefinition>(days.Values);
                ordered.Sort((a, b) => a.Order.CompareTo(b.Order));
                for (int i = 1; i < ordered.Count; i++)
                    if (ordered[i - 1].Order == ordered[i].Order)
                        throw new ArgumentException($"Day '{ordered[i].DayId}'.order conflicts with '{ordered[i - 1].DayId}'.");
                return new DayCampaignDefinition(schemaVersion, sequenceId, startingScrip, ordered.ToArray(), campaignRules);
            }
            catch (ArgumentException error)
            {
                throw new ArgumentException($"{source}: {error.Message}", error);
            }
        }

        private static BloodProgressionDefinition ReadBloodProgression(ConfigFields fields, GameBalance balance,
            BloodProgressionDefinition fallback)
        {
            float initialBlood = (float)fields.Number("initialBlood",
                fallback?.InitialBlood ?? balance.Marble.BloodCapacity, optional: true);
            if (initialBlood <= 0) throw fields.Error("initialBlood (expected a positive amount)");
            if (!fields.Has("stages"))
                return new BloodProgressionDefinition(initialBlood, fallback?.Stages ?? Array.Empty<BloodValueStage>());

            var array = fields.Array("stages");
            var stages = new BloodValueStage[array.Count];
            var attribute = balance.Content?.Definition(GameAttribute.BALL_BASE_VALUE);
            for (int i = 0; i < array.Count; i++)
            {
                var stage = ConfigFields.From(array[i], $"{fields.Context}/stages[{i}]");
                double spent = stage.Number("spentBlood");
                double value = stage.Number("baseValue");
                if (i == 0 ? spent != 0 : spent <= stages[i - 1].SpentBlood)
                    throw stage.Error("spentBlood (start at zero and increase at each stage)");
                if (value < (attribute?.MinValue ?? 0) || value > (attribute?.MaxValue ?? float.MaxValue) ||
                    (i > 0 && value < stages[i - 1].BaseValue))
                    throw stage.Error("baseValue (expected a nondecreasing value in the BALL_BASE_VALUE range)");
                stages[i] = new BloodValueStage(spent, value);
            }
            return new BloodProgressionDefinition(initialBlood, Array.AsReadOnly(stages));
        }

        private static DailyLoadoutDefinition ReadDailyLoadout(ConfigFields day, Dictionary<uint, DeviceDefinition> devices)
        {
            var unlocked = ReadPegIds(day, "unlockedPegIds", devices.ContainsKey, optional: false);
            foreach (uint id in unlocked)
                if (devices[id].PurchaseQuantity != 1 || devices[id].PlacementRadius <= 0)
                    throw new ArgumentException($"{day.Context}.unlockedPegIds: device {id} must support individual placement.");
            var defaults = ReadPegIds(day, "defaultPegIds", id => unlocked.Contains(id));
            var required = ReadPegIds(day, "requiredPegIds", id => defaults.Contains(id));
            int max = (int)ReadInteger(day, "maxLoadoutTypeCount", 1, Math.Min(200, unlocked.Length));
            int min = day.Has("minLoadoutTypeCount") ? (int)ReadInteger(day, "minLoadoutTypeCount", 0, max) : 1;
            if (required.Length > max) throw day.Error("requiredPegIds (exceeds maxLoadoutTypeCount)");
            var mode = ReadEnum<LoadoutSelectionMode>(day.String("loadoutSelectionMode",
                nameof(LoadoutSelectionMode.Manual), optional: true), day.Context + ".loadoutSelectionMode");
            long scrap = ReadInteger(day, "initialBiomassScrap", 0, CampaignWalletSystem.MaxBalance);
            long reward = ReadInteger(day, "clearCompanyScripReward", 0, CampaignWalletSystem.MaxBalance);
            var shop = ReadDailyShop(day, unlocked, defaults);
            return new DailyLoadoutDefinition(unlocked, defaults, required, max, min, mode, scrap, reward, shop);
        }

        private static DailyShopDefinition ReadDailyShop(ConfigFields day, uint[] unlocked, uint[] defaults)
        {
            if (day.String("shopCurrencyType", nameof(GameCurrency.CompanyScrip), optional: true) != nameof(GameCurrency.CompanyScrip))
                throw day.Error("shopCurrencyType (expected CompanyScrip)");
            if (!day.Bool("shopEnabled", false)) return null;

            bool CanOffer(uint id) => unlocked.Contains(id) && !defaults.Contains(id);
            var guaranteed = ReadPegIds(day, "shopGuaranteedPegIds", CanOffer);
            var pool = day.Has("shopRandomPool")
                ? ReadPegIds(day, "shopRandomPool", CanOffer) : unlocked.Where(CanOffer).ToArray();
            int offerCount = (int)ReadInteger(day, "shopOfferCount", Math.Max(1, guaranteed.Length),
                Math.Min(200, pool.Union(guaranteed).Count()));
            long drawCost = ReadInteger(day, "shopDrawCost", 1, CampaignWalletSystem.MaxBalance);
            long? refreshCost = day.Has("shopRefreshCost")
                ? ReadInteger(day, "shopRefreshCost", 1, CampaignWalletSystem.MaxBalance) : (long?)null;
            int limit = day.Has("shopSelectionLimit") ? (int)ReadInteger(day, "shopSelectionLimit", 1, 200) : 200;
            uint seed = day.Has("shopRandomSeed") ? (uint)ReadInteger(day, "shopRandomSeed", 0, uint.MaxValue) : 0;
            var scope = ReadEnum<PegPermissionScope>(day.String("pegPermissionScope",
                nameof(PegPermissionScope.CampaignPermanent), optional: true), day.Context + ".pegPermissionScope");
            var weights = Enum.GetValues(typeof(DeviceRarity)).Cast<DeviceRarity>().Select(DeviceRarityRules.ShopWeight).ToArray();
            if (day.Has("shopRarityWeights"))
            {
                var values = day.Array("shopRarityWeights");
                if (values.Count != weights.Length) throw day.Error("shopRarityWeights (expected common, uncommon, rare, legendary)");
                for (int i = 0; i < values.Count; i++)
                {
                    if (!(values[i] is double number) || number < 1 || number > 100000 || number != Math.Truncate(number))
                        throw day.Error($"shopRarityWeights[{i}]");
                    weights[i] = (int)number;
                }
            }
            return new DailyShopDefinition(drawCost, refreshCost, offerCount, limit, seed, scope, guaranteed, pool, weights);
        }

        private static uint[] ReadPegIds(ConfigFields day, string field, Predicate<uint> allowed, bool optional = true)
        {
            var values = day.Array(field, optional);
            var ids = new uint[values.Count];
            var seen = new HashSet<uint>();
            for (int i = 0; i < values.Count; i++)
            {
                if (!(values[i] is double value) || value < 1 || value > uint.MaxValue || value != Math.Truncate(value) ||
                    !allowed((uint)value) || !seen.Add((uint)value))
                    throw day.Error($"{field}[{i}] (expected a unique eligible device ID)");
                ids[i] = (uint)value;
            }
            return ids;
        }

        private static ProductionFeature ReadProductionFeatures(ConfigFields entry, string field)
        {
            var features = ProductionFeature.None;
            string path = entry.Context + "." + field;
            foreach (var value in entry.Array(field, optional: true))
            {
                var flag = ReadEnum<ProductionFeature>(value as string, path);
                if (flag == ProductionFeature.None || flag == ProductionFeature.All || (features & flag) != 0)
                    throw new ArgumentException($"{path}: expected unique individual features, got '{value}'.");
                features |= flag;
            }
            return features;
        }

        private static MorningPrepDefinition[] ReadMorningPrep(ConfigFields day)
        {
            var input = day.Array("morningPrep", optional: true);
            var result = new MorningPrepDefinition[input.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < input.Count; i++)
            {
                string path = $"{day.Context}.morningPrep[{i}]";
                var entry = ConfigFields.From(input[i], path);
                string id = Id(entry.String("objectiveId"), path + ".objectiveId");
                if (!ids.Add(id)) throw new ArgumentException($"{path}: duplicate Morning Prep objective '{id}'.");
                var interaction = ReadEnum<MorningPrepInteraction>(entry.String("type"), path + ".type");
                string anchor = Id(entry.String("anchorId"), path + ".anchorId");
                string text = entry.String("text");
                if (string.IsNullOrWhiteSpace(text)) throw entry.Error("text");
                result[i] = new MorningPrepDefinition(id, interaction, anchor, text);
            }
            return result;
        }

        private static long ReadInteger(ConfigFields entry, string field, long min, long max)
        {
            double value = entry.Number(field);
            if (value < min || value > max || value != Math.Truncate(value)) throw entry.Error(field);
            return (long)value;
        }

        private static DayEventDefinition ReadEvent(ConfigFields entry, string id, DayBehaviorRegistry behaviors)
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
                var action = ConfigFields.From(input[i], $"{path}.actions[{i}]");
                actions[i] = behaviors.ReadAction(action.String("type"), action.String("parameters", optional: true), action.Context);
            }
            return new DayEventDefinition(id, trigger, entry.Int("priority", optional: true), policy, condition, actions);
        }

        private static DayRuleDefinition ReadRule(ConfigFields entry, string id, GameBalance balance, DayBehaviorRegistry behaviors)
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

        private static Dictionary<string, T> ReadIndex<T>(ConfigFields root, string field, string idField, Func<ConfigFields, string, T> read,
            bool required = false)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            var input = root.Array(field, optional: !required);
            for (int i = 0; i < input.Count; i++)
            {
                string path = $"{field}[{i}]";
                var item = ConfigFields.From(input[i], path);
                string id = Id(item.String(idField), path + "." + idField);
                path += $" ('{id}')";
                if (result.ContainsKey(id)) throw new ArgumentException($"{path}: duplicate ID '{id}'.");
                result.Add(id, read(new ConfigFields(item.Fields, path), id));
            }
            return result;
        }

        private static T[] ReadRefs<T>(ConfigFields entry, string field, Dictionary<string, T> index)
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
