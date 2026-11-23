using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MarblesECS.Tests
{
    public sealed class DayFrameworkTests
    {
        private const string First = "{\"dayId\":\"fixture-first\",\"order\":1,\"productionPlanId\":\"single\"}";
        private const string Second = "{\"dayId\":\"fixture-second\",\"order\":2,\"productionPlanId\":\"multiple\"}";
        private const string Plans = "[{\"planId\":\"single\",\"firstStageId\":1,\"lastStageId\":1}," +
            "{\"planId\":\"multiple\",\"firstStageId\":1,\"lastStageId\":2}]";

        [Test]
        public void SequenceLoadsInOrderAndEmptyResourcePreservesLegacySession()
        {
            var balance = GameBalance.Default();
            var definition = GameDaysLoader.Parse(Json(Second + "," + First), balance);
            Assert.That(definition.IsConfigured, Is.True);
            Assert.That(definition.Days[0].DayId, Is.EqualTo("fixture-first"));
            Assert.That(definition.Days[1].DayId, Is.EqualTo("fixture-second"));
            Assert.That(definition.Days[1].ProductionPlan.LastStageId, Is.EqualTo(2));
            Assert.Throws<NotSupportedException>(() => ((IList<DayDefinition>)definition.Days)[0] = null);

            var empty = GameDaysLoader.Load(balance);
            Assert.That(empty.IsConfigured, Is.False);
            using var simulation = new MarbleSimulation(balance);
            var physics = new PhysicsStub();
            simulation.StartSession(physics);
            Assert.That(simulation.BeginRound(), Is.True);
            int round = simulation.Snapshot.RoundId;
            Assert.That(simulation.TryStartDayCampaign(empty, physics, out var error), Is.False);
            StringAssert.Contains("unconfigured", error);
            Assert.That(simulation.Day.Status, Is.EqualTo(DayCampaignStatus.NotStarted));
            Assert.That(simulation.Snapshot.RoundId, Is.EqualTo(round));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Playing));
        }

        [Test]
        public void InvalidContentNamesTheEntryAndReference()
        {
            var balance = GameBalance.Default();
            var behaviors = Recording((_, __) => { });
            string evt = Event("fixture-event", "WakeUpEntered", Handler("record", "ok"));
            string rule = FeatureRule("fixture-rule");
            behaviors.RegisterFeature("fixture-feature", raw => raw, _ => new FeatureProbe());
            ExpectInvalid(Json(First + "," + First), balance, behaviors, "duplicate ID 'fixture-first'");
            ExpectInvalid(Json(First + "," + Second.Replace("\"order\":2", "\"order\":1")), balance, behaviors, ".order conflicts");
            ExpectInvalid(Json(First, "[" + evt + "," + evt + "]"), balance, behaviors, "duplicate ID 'fixture-event'");
            ExpectInvalid(Json(First, rules: "[" + rule + "," + rule + "]"), balance, behaviors, "duplicate ID 'fixture-rule'");
            ExpectInvalid(Json(Refs(First, "eventIds", "missing")), balance, behaviors, "fixture-first').eventIds[0]: unknown reference 'missing'");
            ExpectInvalid(Json(Refs(First, "ruleIds", "missing")), balance, behaviors, "fixture-first').ruleIds[0]: unknown reference 'missing'");
            ExpectInvalid(Json(First.Replace("single", "missing")), balance, behaviors, ".productionPlanId: unknown reference");
            ExpectInvalid(Json(First, plans: Plans.Replace("\"lastStageId\":2", "\"lastStageId\":99")), balance, behaviors, "lastStageId: unknown stage 99");
            ExpectInvalid(Json(First, "[" + evt.Replace("record", "missing") + "]"), balance, behaviors, "actions[0].type: unknown type 'missing'");
            ExpectInvalid(Json(First, rules: "[" + rule.Replace("fixture-feature", "missing") + "]"), balance, behaviors, "fixture-rule').type: unknown type 'missing'");
            ExpectInvalid(Json(First, "[" + evt.Replace("WakeUpEntered", "42") + "]"), balance, behaviors, ".trigger: unknown DayTrigger '42'");
            ExpectInvalid("{\"schemaVersion\":1,\"sequenceId\":\"fixture\"}", balance, behaviors, "invalid/missing days");
            ExpectInvalid(Json(First) + "garbage", balance, behaviors, "Invalid JSON");
            ExpectInvalid(Json(First).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), balance, behaviors, "Duplicate JSON field");
            ExpectInvalid("this is not JSON", balance, behaviors, "GameDays.json");
            var contentBalance = GameBalanceLoader.Load();
            string attribute = AttributeRule("typed", "Day", "Add", 2);
            ExpectInvalid(Json(First, rules: "[" + attribute.Replace(",\"value\":2", "") + "]"),
                contentBalance, behaviors, "invalid/missing value");
            ExpectInvalid(Json(First, rules: "[" + attribute.Replace("\"value\":2", "\"value\":\"2\"") + "]"),
                contentBalance, behaviors, "invalid/missing value");
        }

        [Test]
        public void ProductionRoundsAndDayTransitionsAdvanceIndependentlyAndRejectStaleCommands()
        {
            var balance = GameBalance.Default();
            var seen = new List<(DayTrigger trigger, string name)>();
            var behaviors = Recording((notice, value) => seen.Add((notice.Trigger, value)));
            string events = "[" + Event("round", "RoundSettled", Handler("record", "round"), policy: "EveryOccurrence") + "," +
                Event("once", "RoundSettled", Handler("record", "once")) + "," +
                Event("settlement", "DaySettlementEntered", Handler("record", "day")) + "," +
                Event("drain", "DrainingEntered", Handler("record", "drain"), policy: "EveryOccurrence") + "]";
            var definition = GameDaysLoader.Parse(Json(First + "," + Refs(Second, "eventIds", "round", "once", "settlement", "drain"), events),
                balance, behaviors);

            using var simulation = new MarbleSimulation(balance);
            var physics = new PhysicsStub();
            Start(simulation, definition, physics);
            long firstWake = simulation.Day.Version;
            Assert.That(simulation.TryCompleteRest(firstWake, out _), Is.False);
            Assert.That(simulation.TryCompleteDaySettlement(firstWake, out _), Is.False);
            Assert.That(simulation.BeginRound(), Is.False);
            Assert.That(simulation.TryCompleteWakeUp(firstWake, out _), Is.True);
            Assert.That(simulation.TryCompleteWakeUp(firstWake, out var stale), Is.False);
            StringAssert.Contains("stale", stale);
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.Production));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Build));
            Assert.Throws<InvalidOperationException>(() => simulation.StartSession(physics));
            ScoreRound(simulation, physics, balance.Stages[1].TargetScore);
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Won));
            Assert.That(simulation.Session.StagesCleared, Is.EqualTo(1));
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            Assert.That(simulation.Day.CompletedDays, Is.Zero);
            Assert.That(simulation.Day.SettlementCommitted, Is.False);
            long firstRest = CompleteSettlement(simulation);
            Assert.That(simulation.TryCompleteRest(firstRest, out _), Is.True);
            Assert.That(simulation.Day.DayId, Is.EqualTo("fixture-second"));
            Assert.That(simulation.Day.CompletedDays, Is.EqualTo(1));
            Assert.That(simulation.TryCompleteRest(firstRest, out _), Is.False);

            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            ScoreRound(simulation, physics, balance.Stages[0].TargetScore);
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Shop));
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.Production));
            Assert.That(simulation.Day.SettledRounds, Is.EqualTo(1));
            Assert.That(seen, Is.EqualTo(new[]
            {
                (DayTrigger.DrainingEntered, "drain"), (DayTrigger.RoundSettled, "once"), (DayTrigger.RoundSettled, "round")
            }));
            Assert.That(simulation.TryCompleteDaySettlement(simulation.Day.Version, out _), Is.False);
            Assert.That(simulation.EnterBuild(), Is.True);
            ScoreRound(simulation, physics, balance.Stages[1].TargetScore);
            Assert.That(simulation.Day.SettledRounds, Is.EqualTo(2));
            Assert.That(seen.GetRange(3, 3), Is.EqualTo(new[]
            {
                (DayTrigger.DrainingEntered, "drain"), (DayTrigger.RoundSettled, "round"), (DayTrigger.DaySettlementEntered, "day")
            }));

            long secondRest = CompleteSettlement(simulation);
            Assert.That(simulation.TryCompleteRest(firstRest, out _), Is.False);
            Assert.That(simulation.TryCompleteRest(secondRest, out _), Is.True);
            Assert.That(simulation.Day.Status, Is.EqualTo(DayCampaignStatus.Completed));
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.Rest));
            Assert.That(simulation.Day.DayIndex, Is.EqualTo(1));
            Assert.That(simulation.Day.CompletedDays, Is.EqualTo(2));
            Assert.That(simulation.TryCompleteRest(simulation.Day.Version, out _), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RetryRestoresOnlyCurrentDayWithSelectedSharedRandomPolicy(bool injectSeed)
        {
            var balance = GameBalance.Default();
            var lifecycle = new List<DayTrigger>();
            var behaviors = Recording((notice, _) => lifecycle.Add(notice.Trigger));
            var triggers = new[] { "WakeUpEntered", "ProductionFailed", "ProductionExited", "DayRetried", "DaySettlementEntered", "RestEntered" };
            var definitions = Array.ConvertAll(triggers, trigger => Event(trigger, trigger, Handler("record", trigger)));
            var config = GameDaysLoader.Parse(Json(First + "," + Refs(Second, "eventIds", triggers),
                "[" + string.Join(",", definitions) + "]"), balance, behaviors);
            var physics = new PhysicsStub();
            DayRetryRandomContext? selected = null;
            DayRetryRandomPolicy policy = null;
            if (injectSeed) policy = context => { selected = context; return 83u; };
            using var simulation = new MarbleSimulation(balance);
            Start(simulation, config, physics, policy);
            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            ScoreRound(simulation, physics, balance.Stages[0].TargetScore);
            Assert.That(simulation.TryCompleteRest(CompleteSettlement(simulation), out _), Is.True);
            var baseline = simulation.Day;
            long coins = simulation.Session.Coins;
            Assert.That(simulation.TryCompleteWakeUp(baseline.Version, out _), Is.True);
            Assert.That(simulation.BeginRound(), Is.True);
            var round = simulation.Context.Round;
            MarbleRules.NextRandom(ref round.RandomState);
            simulation.Context.Round = round;
            var player = simulation.Context.Player;
            player.Blood = 0;
            simulation.Context.Player = player;
            Step(simulation, physics);

            var day = simulation.Day;
            Assert.That(day.DayId, Is.EqualTo(baseline.DayId));
            Assert.That(day.CompletedDays, Is.EqualTo(1));
            Assert.That(day.Attempt, Is.EqualTo(2));
            Assert.That(day.Phase, Is.EqualTo(DayPhase.WakeUp));
            Assert.That(day.PreviousPhase, Is.EqualTo(DayPhase.Production));
            Assert.That(day.LastTransition, Is.EqualTo(DayTransitionReason.ProductionFailed));
            Assert.That(day.Result, Is.EqualTo(DayResult.Pending));
            Assert.That(day.SettledRounds, Is.Zero);
            Assert.That(day.RandomState, Is.EqualTo(injectSeed ? 83u : baseline.RandomState));
            Assert.That(day.AttemptStartRandomState, Is.EqualTo(day.RandomState));
            Assert.That(simulation.Session.Coins, Is.EqualTo(coins));
            Assert.That(simulation.Snapshot.RoundId, Is.GreaterThan(round.RoundId));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Build));
            Assert.That(simulation.Snapshot.Blood, Is.EqualTo(balance.Marble.BloodCapacity));
            Assert.That(lifecycle, Is.EqualTo(new[]
            {
                DayTrigger.WakeUpEntered, DayTrigger.ProductionFailed, DayTrigger.ProductionExited, DayTrigger.DayRetried, DayTrigger.WakeUpEntered
            }));
            if (injectSeed)
            {
                Assert.That(selected.Value.CheckpointState, Is.EqualTo(baseline.RandomState));
                Assert.That(selected.Value.FailedAttempt.Result, Is.EqualTo(DayResult.Failed));
                Assert.That(selected.Value.FailedAttempt.RandomState, Is.EqualTo(round.RandomState));
            }
        }

        [Test]
        public void CheckpointRebuildsDeviceReferencesAndRestoresItsBuffers()
        {
            var balance = GameBalanceLoader.Load();
            foreach (var device in balance.Devices) device.StartingCount = 0;
            Array.Find(balance.Devices, device => device.PurchaseQuantity == 1 && device.PlacementRadius > 0).StartingCount = 1;
            var definition = GameDaysLoader.Parse(Json(First + "," + Second), balance);
            using var simulation = new MarbleSimulation(balance);
            var physics = new PhysicsStub();
            Start(simulation, definition, physics);
            var context = simulation.Context;
            int id = simulation.GetOwnedDevices()[0].InstanceId;
            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            Assert.That(simulation.PlaceDevice(id, 2, 3, clearance: 0), Is.True);
            Assert.That(CampaignStateUtility.FindDevice(context, id, out var original), Is.True);
            ulong stableId = context.Manager.GetComponentData<StableIdentity>(original).StableId;
            DeviceInstanceState.Set(context, original, "fixture", 42);
            DeviceDurabilitySystem.SetLimit(context, original, 7);
            Assert.That(simulation.ApplyDeviceAttribute(id, 3, GameAttribute.MACHINE_MULT, AttributeOperation.Set, 10), Is.True);
            Assert.That(simulation.ApplyGlobalAttribute(4, GameAttribute.GLOBAL_MULT, AttributeOperation.Add, 1), Is.True);
            ScoreRound(simulation, physics, balance.Stages[0].TargetScore);
            int firstProductionRoundOfNextDay = context.Round.RoundId + 2;
            DeviceRoundState.Write(context, original, firstProductionRoundOfNextDay,
                new Dictionary<string, double> { { "fixture-upcoming", 27 } });
            context.DeviceScripts.SetStatus(id, firstProductionRoundOfNextDay, "fixture status");
            Assert.That(simulation.TryCompleteRest(CompleteSettlement(simulation), out _), Is.True);
            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            DeviceDurabilitySystem.SetLimit(context, original, 1);
            Assert.That(simulation.SellDevice(id), Is.True);
            Assert.That(simulation.ApplyGlobalAttribute(4, GameAttribute.GLOBAL_MULT, AttributeOperation.Add, 8), Is.True);
            Assert.That(simulation.BeginRound(), Is.True);
            var player = context.Player;
            player.Blood = 0;
            context.Player = player;
            Step(simulation, physics);

            Assert.That(simulation.Day.Attempt, Is.EqualTo(2));
            Assert.That(context.Manager.Exists(original), Is.False);
            Assert.That(CampaignStateUtility.FindDevice(context, id, out var rebuilt), Is.True);
            Assert.That(context.Manager.GetComponentData<StableIdentity>(rebuilt).StableId, Is.EqualTo(stableId));
            Assert.That(context.Manager.GetComponentData<Owner>(rebuilt).Player, Is.EqualTo(context.PlayerEntity));
            Assert.That(context.Manager.GetComponentData<OnBoard>(rebuilt).Board, Is.EqualTo(context.BoardEntity));
            Assert.That(context.Manager.GetComponentData<DefinitionRef>(rebuilt).DefinitionId,
                Is.EqualTo(context.Manager.GetComponentData<OwnedDeviceData>(rebuilt).DefinitionId));
            Assert.That(DeviceInstanceState.Get(context, rebuilt, "fixture"), Is.EqualTo(42));
            Assert.That(DeviceRoundState.Get(context, rebuilt, "fixture-upcoming"), Is.EqualTo(27));
            Assert.That(simulation.GetDeviceStatus(id), Is.EqualTo("fixture status"));
            Assert.That(simulation.GetOwnedDevices()[0].Durability, Is.EqualTo(7));
            Assert.That(AttributeRuntime.Device(context, rebuilt, GameAttribute.MACHINE_MULT), Is.EqualTo(10));
            Assert.That(simulation.GetGlobalAttribute(GameAttribute.GLOBAL_MULT), Is.EqualTo(2));
        }

        [Test]
        public void DuplicateNotificationsAndConditionsDoNotDuplicateActions()
        {
            var calls = new List<string>();
            int conditionCalls = 0;
            var behaviors = Recording((_, name) => calls.Add(name));
            behaviors.RegisterCondition("after-first", raw => raw, (notice, _) => { conditionCalls++; return notice.Occurrence > 1; });
            string events = "[" + Event("every", "BuildEntered", Handler("record", "every"), policy: "EveryOccurrence") + "," +
                Event("once", "BuildEntered", Handler("record", "once"), condition: Handler("after-first", "")) + "]";
            var balance = GameBalance.Default();
            var definition = GameDaysLoader.Parse(Json(Refs(First, "eventIds", "once", "every"), events), balance, behaviors);
            using var simulation = new MarbleSimulation(balance);
            using var rules = new DayRuleSystem(simulation.Context);
            using var runner = new DayEventSystem(simulation.Context, rules);
            runner.SetDay(definition.Days[0]);
            for (long occurrence = 1; occurrence <= 3; occurrence++)
            {
                var notice = new DayLifecycleNotice(default, DayTrigger.BuildEntered, occurrence, RoundPhase.Build, 1);
                runner.Publish(notice);
                runner.Publish(notice);
                Assert.That(runner.Drain(), Is.True);
                runner.Publish(notice);
                Assert.That(runner.Drain(), Is.True);
            }
            Assert.That(calls, Is.EqualTo(new[] { "every", "every", "once", "every" }));
            Assert.That(conditionCalls, Is.EqualTo(2));
        }

        [Test]
        public void AsyncActionsPreserveOrderAndBlockPhaseExitUntilComplete()
        {
            var calls = new List<string>();
            var wait = new TaskCompletionSource<bool>();
            var behaviors = Recording((_, name) => calls.Add(name));
            behaviors.RegisterAction("wait", raw => raw, (_, name, __) => { calls.Add(name); return wait.Task; });
            string events = "[" +
                Event("high", "WakeUpEntered", Handler("record", "first") + "," + Handler("wait", "pending") + "," + Handler("record", "third"), 4) + "," +
                Event("low", "WakeUpEntered", Handler("record", "fourth")) + "," +
                Event("exit", "WakeUpExited", Handler("record", "exit")) + "]";
            var balance = GameBalance.Default();
            var config = GameDaysLoader.Parse(Json(Refs(First, "eventIds", "exit", "low", "high"), events), balance, behaviors);
            using var simulation = new MarbleSimulation(balance);
            Start(simulation, config, new PhysicsStub());
            long version = simulation.Day.Version;
            Assert.That(calls, Is.EqualTo(new[] { "first", "pending" }));
            Assert.That(simulation.Day.LifecyclePending, Is.True);
            Assert.That(simulation.TryCompleteWakeUp(version, out var busy), Is.False);
            StringAssert.Contains("still processing", busy);
            wait.SetResult(true);
            simulation.PumpDayLifecycle();
            Assert.That(simulation.TryCompleteWakeUp(version, out _), Is.True);
            Assert.That(simulation.TryCompleteWakeUp(version, out _), Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "first", "pending", "third", "fourth", "exit" }));
        }

        [Test]
        public void FailedActionAbortsOnlyItsEventAndDisposalCancelsPendingAction()
        {
            var calls = new List<string>();
            CancellationToken cancellation = default;
            DayActionContext pending = null;
            var wait = new TaskCompletionSource<bool>();
            var behaviors = Recording((_, name) => calls.Add(name));
            behaviors.RegisterAction("fail", raw => raw, (_, __, ___) => Task.FromException(new InvalidOperationException("fixture failure")));
            behaviors.RegisterAction("wait", raw => raw, (context, _, token) => { pending = context; cancellation = token; return wait.Task; });
            string events = "[" +
                Event("fail", "WakeUpEntered", Handler("fail", "") + "," + Handler("record", "unreachable"), 2) + "," +
                Event("next", "WakeUpEntered", Handler("record", "next") + "," + Handler("wait", "")) + "]";
            var balance = GameBalance.Default();
            var config = GameDaysLoader.Parse(Json(Refs(First, "eventIds", "fail", "next"), events), balance, behaviors);
            var simulation = new MarbleSimulation(balance);
            LogAssert.Expect(LogType.Error, new Regex("event 'fail'.*fixture failure"));
            try
            {
                Start(simulation, config, new PhysicsStub());
                Assert.That(calls, Is.EqualTo(new[] { "next" }));
                Assert.That(simulation.Day.LifecyclePending, Is.True);
            }
            finally { simulation.Dispose(); }
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.Throws<InvalidOperationException>(() => pending.RemoveRules("fixture", DayRuleScope.Day));
            wait.TrySetCanceled();
        }

        [Test]
        public void RuleScopesReuseAttributeMergeAndReleaseFeaturesOnRetryAndDisposal()
        {
            var balance = GameBalanceLoader.Load();
            var created = new List<FeatureProbe>();
            var behaviors = new DayBehaviorRegistry();
            behaviors.RegisterFeature("fixture-feature", raw => raw, _ =>
            {
                var feature = new FeatureProbe();
                created.Add(feature);
                return feature;
            });
            int removed = 0;
            behaviors.RegisterAction("remove", raw => raw, (context, _, __) =>
            {
                removed += context.RemoveRules(context.Notice.Day.DayId, DayRuleScope.Day);
                return Task.CompletedTask;
            });
            string rules = "[" +
                AttributeRule("add", "Campaign", "Add", 2) + "," +
                AttributeRule("multiply", "Day", "Multiply", 2) + "," +
                AttributeRule("overwrite-low", "Phase", "Set", 5, ",\"priority\":1,\"phase\":\"WakeUp\"") + "," +
                AttributeRule("overwrite-high", "Phase", "Set", 10, ",\"priority\":2,\"phase\":\"WakeUp\"") + "," +
                AttributeRule("temporary", "Temporary", "Add", 3, ",\"durationTicks\":1") + "," + FeatureRule("feature") + "]";
            string day = Refs(Refs(First, "ruleIds", "multiply", "overwrite-high", "overwrite-low", "temporary", "feature"), "eventIds", "remove");
            string events = "[" + Event("remove", "PlayingEntered", Handler("remove", "")) + "]";
            var definition = GameDaysLoader.Parse(Json(day, events, rules, "[\"add\"]"), balance, behaviors);
            using var simulation = new MarbleSimulation(balance);
            var physics = new PhysicsStub();
            const GameAttribute attribute = GameAttribute.MACHINE_MULT;
            Start(simulation, definition, physics);
            Assert.That(simulation.GetGlobalAttribute(attribute), Is.EqualTo(24));
            Assert.That(created[0].Active, Is.True);
            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            Assert.That(simulation.GetGlobalAttribute(attribute), Is.EqualTo(12));
            Assert.That(simulation.BeginRound(), Is.True);
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(created[0].Disposed, Is.True);
            Assert.That(simulation.GetGlobalAttribute(attribute), Is.EqualTo(6));
            Step(simulation, physics);
            Assert.That(simulation.GetGlobalAttribute(attribute), Is.EqualTo(3));
            var player = simulation.Context.Player;
            player.Blood = 0;
            simulation.Context.Player = player;
            Step(simulation, physics);
            Assert.That(simulation.Day.Attempt, Is.EqualTo(2));
            Assert.That(simulation.GetGlobalAttribute(attribute), Is.EqualTo(24));
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created[1].Active, Is.True);
            simulation.Dispose();
            Assert.That(created[1].Disposed, Is.True);
            Assert.That(created[1].Active, Is.False);
        }

        private static void ExpectInvalid(string json, GameBalance balance, DayBehaviorRegistry behaviors, string expected)
        {
            StringAssert.Contains(expected, Assert.Throws<ArgumentException>(() => GameDaysLoader.Parse(json, balance, behaviors)).Message);
        }

        private static string Json(string days, string events = "[]", string rules = "[]", string campaignRules = "[]", string plans = Plans) =>
            $"{{\"schemaVersion\":1,\"sequenceId\":\"fixture\",\"days\":[{days}],\"productionPlans\":{plans}," +
            $"\"events\":{events},\"rules\":{rules},\"campaignRuleIds\":{campaignRules}}}";

        private static string Refs(string day, string field, params string[] ids) =>
            day.Substring(0, day.Length - 1) + ",\"" + field + "\":[\"" + string.Join("\",\"", ids) + "\"]}";

        private static string Event(string id, string trigger, string actions, int priority = 0,
            string policy = "OncePerAttempt", string condition = null) =>
            $"{{\"eventId\":\"{id}\",\"trigger\":\"{trigger}\",\"actions\":[{actions}],\"priority\":{priority}," +
            $"\"executionPolicy\":\"{policy}\"" + (condition == null ? "}" : ",\"condition\":" + condition + "}");

        private static string Handler(string type, string value) => $"{{\"type\":\"{type}\",\"parameters\":\"{value}\"}}";
        private static string FeatureRule(string id) => $"{{\"ruleId\":\"{id}\",\"type\":\"fixture-feature\",\"scope\":\"Day\"}}";
        private static string AttributeRule(string id, string scope, string operation, int value, string extra = "") =>
            $"{{\"ruleId\":\"{id}\",\"type\":\"Attribute\",\"scope\":\"{scope}\",\"attribute\":\"MACHINE_MULT\"," +
            $"\"operation\":\"{operation}\",\"value\":{value}{extra}}}";

        private static DayBehaviorRegistry Recording(Action<DayLifecycleNotice, string> record)
        {
            var registry = new DayBehaviorRegistry();
            registry.RegisterAction("record", raw => raw, (context, value, _) => { record(context.Notice, value); return Task.CompletedTask; });
            return registry;
        }

        private static void Start(MarbleSimulation simulation, DayCampaignDefinition definition, PhysicsStub physics,
            DayRetryRandomPolicy policy = null)
        {
            Assert.That(simulation.TryStartDayCampaign(definition, physics, out var error, 17, policy), Is.True, error);
        }

        private static long CompleteSettlement(MarbleSimulation simulation)
        {
            long version = simulation.Day.Version;
            Assert.That(simulation.TryCompleteDaySettlement(version, out _), Is.True);
            Assert.That(simulation.TryCompleteDaySettlement(version, out _), Is.False);
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.Rest));
            Assert.That(simulation.Day.SettlementCommitted, Is.True);
            return simulation.Day.Version;
        }

        private static void ScoreRound(MarbleSimulation simulation, PhysicsStub physics, long score)
        {
            Assert.That(simulation.BeginRound(), Is.True);
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Playing));
            simulation.FireOnce();
            simulation.BeforePhysics(.01f, physics, Vector3.zero, Quaternion.identity);
            simulation.EnqueueContact(new MarbleContact
            {
                Key = physics.Last, TargetId = 1, Kind = MarbleContactKind.Score, FlatScore = score, Multiplier = 1
            });
            simulation.AfterPhysics(physics);
            Assert.That(simulation.CashOut(), Is.True);
        }

        private static void Step(MarbleSimulation simulation, PhysicsStub physics)
        {
            simulation.BeforePhysics(.01f, physics, Vector3.zero, Quaternion.identity);
            simulation.AfterPhysics(physics);
        }

        private sealed class PhysicsStub : IMarbleDayPhysics, IMarbleDayPhysicsCheckpoint
        {
            private readonly Dictionary<MarbleKey, Vector3> live = new Dictionary<MarbleKey, Vector3>();
            internal MarbleKey Last { get; private set; }
            public bool TrySpawn(MarbleSpawnData data) { Last = data.Key; live.Add(data.Key, data.Position); return true; }
            public bool TryGetPosition(MarbleKey key, out Vector3 position) => live.TryGetValue(key, out position);
            public void Remove(MarbleKey key) => live.Remove(key);
            public void ResetDayPhysics() => live.Clear();
            public IMarbleDayPhysicsCheckpoint CaptureDayCheckpoint() => this;
            public void Restore() => ResetDayPhysics();
        }

        private sealed class FeatureProbe : IDayFeature
        {
            internal bool Active { get; private set; }
            internal bool Disposed { get; private set; }
            public void Activate(DayProgressSnapshot day) => Active = true;
            public void Deactivate() => Active = false;
            public void Dispose() => Disposed = true;
        }
    }
}
