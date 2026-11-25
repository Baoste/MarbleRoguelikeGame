using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MarblesECS.PhysX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MarblesECS.Tests
{
    public sealed class DayFrameworkTests
    {
        private const string Features = ",\"productionFeatures\":[\"Shop\",\"DevicePlacement\",\"Rush\",\"GamblingWheel\"]";
        private const int FirstTarget = 150, SecondTarget = 450;
        private const string First = "{\"dayId\":\"fixture-first\",\"order\":1,\"targetScore\":150,\"rewardCoins\":35" + Features + "}";
        private const string Second = "{\"dayId\":\"fixture-second\",\"order\":2,\"targetScore\":450,\"rewardCoins\":45" + Features + "}";

        [Test]
        public void SequenceLoadsInOrderAndEmptyResourcePreservesLegacySession()
        {
            var balance = GameBalance.Default();
            var definition = GameDaysLoader.Parse(Json(Second + "," + First), balance);
            Assert.That(definition.IsConfigured, Is.True);
            Assert.That(definition.Days[0].DayId, Is.EqualTo("fixture-first"));
            Assert.That(definition.Days[1].DayId, Is.EqualTo("fixture-second"));
            Assert.That(definition.Days[1].TargetScore, Is.EqualTo(SecondTarget));
            Assert.That(definition.Days[1].RewardCoins, Is.EqualTo(45));
            Assert.Throws<NotSupportedException>(() => ((IList<DayDefinition>)definition.Days)[0] = null);

            var empty = GameDaysLoader.Parse(Json(string.Empty), balance);
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
        public void DayZeroRequiresItsNoteAndPaysItsOwnRewardAndBloodDividend()
        {
            var balance = GameBalanceLoader.Load();
            var definition = GameDaysLoader.Load(balance);
            var day0 = definition.Days[0];
            Assert.That(day0.DayId, Is.EqualTo("day0"));
            Assert.That(day0.BoardLayoutFile, Is.EqualTo("Day0.json"));
            Assert.That(day0.BoardLayout.Pins, Has.Count.EqualTo(3));
            Assert.That(day0.BoardLayout.Zones, Has.Count.EqualTo(1));
            Assert.That(day0.RewardCoins, Is.EqualTo(22));
            Assert.That(day0.ProductionFeatures, Is.EqualTo(ProductionFeature.None));
            Assert.That(day0.MorningPrep, Has.Count.EqualTo(1));
            var note = day0.MorningPrep[0];
            Assert.That(note.Text, Does.StartWith("欢迎入职"));
            Assert.That(note.Text, Does.Contain($"今日生产指标为 {day0.TargetScore} 分"));
            using var simulation = new MarbleSimulation(balance);
            var physics = new PhysicsStub();
            Start(simulation, definition, physics);
            Assert.That(physics.CurrentLayout, Is.SameAs(day0.BoardLayout));
            long wake = simulation.Day.Version;
            Assert.That(simulation.Day.MorningPrepRemaining, Is.EqualTo(1));
            Assert.That(simulation.TryCompleteWakeUp(wake, out string error), Is.False);
            StringAssert.Contains("Morning Prep", error);
            Assert.That(simulation.TryCompleteMorningPrep(note.ObjectiveId, wake - 1), Is.False);
            Assert.That(simulation.TryCompleteMorningPrep("unknown", wake), Is.False);
            Assert.That(simulation.TryCompleteMorningPrep(note.ObjectiveId, wake), Is.True);
            Assert.That(simulation.TryCompleteMorningPrep(note.ObjectiveId, wake), Is.False);
            Assert.That(simulation.TryCompleteWakeUp(wake, out _), Is.True);
            Assert.That(simulation.BeginRound(), Is.True);
            Assert.That(simulation.Session.TargetScore, Is.EqualTo(50));

            var player = simulation.Context.Player;
            player.Blood = 0;
            simulation.Context.Player = player;
            Step(simulation, physics);
            Assert.That(simulation.Day.Attempt, Is.EqualTo(2));
            Assert.That(physics.LayoutChanges, Is.EqualTo(1), "retry keeps the generated geometry");
            Assert.That(simulation.Day.MorningPrepRemaining, Is.EqualTo(1));
            Assert.That(simulation.IsMorningPrepComplete(note.ObjectiveId), Is.False);
            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.False);
            Assert.That(simulation.TryCompleteMorningPrep(note.ObjectiveId, simulation.Day.Version), Is.True);
            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            Assert.That(simulation.BeginRound(), Is.True);
            var round = simulation.Context.Round;
            round.Score = 49;
            simulation.Context.Round = round;
            Assert.That(simulation.CashOut(), Is.False);
            round.Score = 50;
            simulation.Context.Round = round;
            player = simulation.Context.Player;
            player.Blood = 40;
            simulation.Context.Player = player;
            double coinRate = balance.Content.Definition(GameAttribute.COIN_GAIN_RATE).DefaultValue;
            double dividendRate = balance.Content.Definition(GameAttribute.BLOOD_DIVIDEND_RATE).DefaultValue;
            long reward = (long)Math.Floor(day0.RewardCoins * coinRate);
            long cashout = (long)Math.Floor(40 * (dividendRate * coinRate));
            long wallet = balance.Campaign.StartingCoins + reward + cashout;
            Assert.That(simulation.CashOut(), Is.True);
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            Assert.That(simulation.Session.LastCashoutCoins, Is.EqualTo(cashout));
            Assert.That(simulation.Session.LastRewardCoins, Is.EqualTo(reward));
            Assert.That(simulation.Session.Coins, Is.EqualTo(wallet));
            Assert.That(simulation.CashOut(), Is.False);
            Assert.That(simulation.TryCompleteRest(CompleteSettlement(simulation), out _), Is.True);
            Assert.That(simulation.Session.Coins, Is.EqualTo(wallet));
            Assert.That(simulation.Day.DayId, Is.EqualTo("day1"));
            Assert.That(simulation.Session.TargetScore, Is.EqualTo(200));
            Assert.That(simulation.CurrentDayDefinition.ProductionFeatures,
                Is.EqualTo(ProductionFeature.Shop | ProductionFeature.DevicePlacement));
        }

        [Test]
        public void BoardLayoutJsonRoundTripsBothZoneTypesAndProjectsFromTopLeft()
        {
            var layout = BoardLayoutLoader.Load("Day0.json");
            var pins = new List<BoardLayoutPin>(layout.Pins);
            var zones = new List<BoardLayoutZone>(layout.Zones);
            zones.Add(new BoardLayoutZone
            {
                type = "RandomScore", x = .2f, y = .7f, width = .6f, depth = .8f, triggerHeight = .75f,
                multiplier = 2.5f, flatScore = 3, priority = -1, rushEnabled = true, scoreStackEnabled = false
            });
            var pin = pins[0];
            pin.rushChanceAdd = .25f;
            pins[0] = pin;
            var saved = BoardLayoutLoader.Parse(BoardLayoutLoader.ToJson(pins, zones));
            Assert.That(saved.Pins, Is.EqualTo(pins));
            Assert.That(saved.Zones, Is.EqualTo(zones));
            Assert.That(saved.Zones[0].Kind, Is.EqualTo(MarbleContactKind.Score));
            Assert.That(saved.Zones[1].Kind, Is.EqualTo(MarbleContactKind.RandomScore));
            var projection = new BoardLayoutProjection(8, 6, 10);
            projection.Validate(saved);
            Assert.That(projection.ToSurfaceLocal(0, 0, 0), Is.EqualTo(new Vector3(4, 0, 3)));
            Assert.That(projection.ToSurfaceLocal(1, 1, 0), Is.EqualTo(new Vector3(-4, 0, -5)));
            var position = projection.ToNormalized(projection.ToPlane(.25f, .75f));
            Assert.That(position.x, Is.EqualTo(.25f).Within(.00001f));
            Assert.That(position.y, Is.EqualTo(.75f).Within(.00001f));

            pin.x = 0;
            pins[0] = pin;
            var outside = BoardLayoutLoader.Parse(BoardLayoutLoader.ToJson(pins, zones));
            StringAssert.Contains("pins[0]", Assert.Throws<ArgumentException>(() => projection.Validate(outside)).Message);
        }

        [Test]
        public void DefaultDayProductionScoresTheWheelLaneDirectlyAndSkipsTheShop()
        {
            var balance = GameBalanceLoader.Load();
            var definition = GameDaysLoader.Parse(Json(Second.Replace(Features, "")), balance);
            using var simulation = new MarbleSimulation(balance);
            var physics = new PhysicsStub();
            Start(simulation, definition, physics);
            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            Assert.That(simulation.HasProductionFeature(ProductionFeature.Shop), Is.False);
            Assert.That(simulation.HasProductionFeature(ProductionFeature.DevicePlacement), Is.False);
            Assert.That(simulation.HasProductionFeature(ProductionFeature.Rush), Is.False);
            Assert.That(simulation.HasProductionFeature(ProductionFeature.GamblingWheel), Is.False);
            Assert.That(simulation.PlaceDevice(1, 0, 0), Is.False);
            Assert.That(simulation.BeginRound(), Is.True);
            uint random = simulation.Day.RandomState;
            simulation.FireOnce();
            simulation.BeforePhysics(.01f, physics, Vector3.zero, Quaternion.identity);
            simulation.EnqueueContact(new MarbleContact
            {
                Key = physics.Last, TargetId = 1, Kind = MarbleContactKind.RandomScore,
                FlatScore = definition.Days[0].TargetScore, Multiplier = 1
            });
            simulation.AfterPhysics(physics);
            Assert.That(simulation.TryDequeueScore(out var score), Is.True);
            Assert.That(score.IsPending, Is.False);
            Assert.That(simulation.GamblingWheel.BallCount, Is.Zero);
            Assert.That(simulation.GamblingWheel.DrawCount, Is.Zero);
            Assert.That(simulation.Snapshot.RushMeter, Is.Zero);
            Assert.That(simulation.CashOut(), Is.True);
            Assert.That(simulation.Day.RandomState, Is.EqualTo(random));
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Won));
            Assert.That(simulation.GetShopOffers(), Is.Empty);
            Assert.That(simulation.BuyOffer(0), Is.False);
            Assert.That(simulation.RefreshShop(), Is.False);
            Assert.That(simulation.BeginRound(), Is.False);
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
            ExpectInvalid(Json(First.Replace("\"targetScore\":150,", "")), balance, behaviors, "invalid/missing targetScore");
            ExpectInvalid(Json(First.Replace("\"rewardCoins\":35", "\"rewardCoins\":-1")), balance, behaviors, "invalid/missing rewardCoins");
            ExpectInvalid(Json(First.Replace("\"rewardCoins\":35", "\"rewardCoins\":0.5")), balance, behaviors, "invalid/missing rewardCoins");
            ExpectInvalid(Json(First.Insert(First.Length - 1, ",\"boardLayout\":\"missing.json\"")),
                balance, behaviors, "boardLayout: Board layout 'missing.json' was not found");
            ExpectInvalid(Json(First.Insert(First.Length - 1, ",\"boardLayout\":\"../Day0.json\"")),
                balance, behaviors, "Invalid board layout file '../Day0.json'");
            ExpectInvalid(Json(First, "[" + evt.Replace("record", "missing") + "]"), balance, behaviors, "actions[0].type: unknown type 'missing'");
            ExpectInvalid(Json(First, rules: "[" + rule.Replace("fixture-feature", "missing") + "]"), balance, behaviors, "fixture-rule').type: unknown type 'missing'");
            ExpectInvalid(Json(First, "[" + evt.Replace("WakeUpEntered", "42") + "]"), balance, behaviors, ".trigger: unknown DayTrigger '42'");
            ExpectInvalid("{\"schemaVersion\":2,\"sequenceId\":\"fixture\"}", balance, behaviors, "invalid/missing days");
            ExpectInvalid(Json(First) + "garbage", balance, behaviors, "Invalid JSON");
            ExpectInvalid(Json(First).Replace("\"schemaVersion\":2", "\"schemaVersion\":2,\"schemaVersion\":2"), balance, behaviors, "Duplicate JSON field");
            ExpectInvalid(Json(First).Replace("\"schemaVersion\":2", "\"schemaVersion\":1"), balance, behaviors, "schemaVersion: expected 2");
            ExpectInvalid("this is not JSON", balance, behaviors, "GameDays.json");
            var contentBalance = GameBalanceLoader.Load();
            string attribute = AttributeRule("typed", "Day", "Add", 2);
            ExpectInvalid(Json(First, rules: "[" + attribute.Replace(",\"value\":2", "") + "]"),
                contentBalance, behaviors, "invalid/missing value");
            ExpectInvalid(Json(First, rules: "[" + attribute.Replace("\"value\":2", "\"value\":\"2\"") + "]"),
                contentBalance, behaviors, "invalid/missing value");
        }

        [Test]
        public void EachDayEndsAfterOneProductionRoundAndRejectsStaleCommands()
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
            ScoreRound(simulation, physics, FirstTarget);
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Won));
            Assert.That(simulation.Session.LastRewardCoins, Is.EqualTo(35));
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            Assert.That(simulation.Day.CompletedDays, Is.Zero);
            Assert.That(simulation.Day.SettlementCommitted, Is.False);
            long firstRest = CompleteSettlement(simulation);
            Assert.That(simulation.TryCompleteRest(firstRest, out _), Is.True);
            Assert.That(simulation.Day.DayId, Is.EqualTo("fixture-second"));
            Assert.That(simulation.Day.CompletedDays, Is.EqualTo(1));
            Assert.That(simulation.TryCompleteRest(firstRest, out _), Is.False);

            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            Assert.That(simulation.Snapshot.TargetScore, Is.EqualTo(SecondTarget));
            ScoreRound(simulation, physics, SecondTarget);
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RoundPhase.Won));
            Assert.That(simulation.Session.LastRewardCoins, Is.EqualTo(45));
            Assert.That(simulation.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            Assert.That(simulation.TryOpenShop(simulation.Day.Version), Is.False);
            Assert.That(simulation.Day.SettledRounds, Is.EqualTo(1));
            Assert.That(seen, Is.EqualTo(new[]
            {
                (DayTrigger.DrainingEntered, "drain"), (DayTrigger.RoundSettled, "once"),
                (DayTrigger.RoundSettled, "round"), (DayTrigger.DaySettlementEntered, "day")
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
            ScoreRound(simulation, physics, FirstTarget);
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
            Array.Find(balance.Devices, device => device.PurchaseQuantity == 1 && device.PlacementRadius > 0).StartingCount = 2;
            var definition = GameDaysLoader.Parse(Json(First + "," + Second), balance);
            using var simulation = new MarbleSimulation(balance);
            var physics = new PhysicsStub();
            Start(simulation, definition, physics);
            var context = simulation.Context;
            var inventory = simulation.GetOwnedDevices();
            int id = inventory[0].InstanceId;
            Assert.That(simulation.TryCompleteWakeUp(simulation.Day.Version, out _), Is.True);
            Assert.That(simulation.PlaceDevice(id, 2, 3, clearance: 0), Is.True);
            Assert.That(simulation.PlaceDevice(inventory[1].InstanceId, -2, -3, clearance: 0), Is.True);
            Assert.That(CampaignStateUtility.FindDevice(context, id, out var original), Is.True);
            ulong stableId = context.Manager.GetComponentData<StableIdentity>(original).StableId;
            DeviceInstanceState.Set(context, original, "fixture", 42);
            DeviceDurabilitySystem.SetLimit(context, original, 7);
            Assert.That(simulation.ApplyDeviceAttribute(id, 3, GameAttribute.MACHINE_MULT, AttributeOperation.Set, 10), Is.True);
            Assert.That(simulation.ApplyGlobalAttribute(4, GameAttribute.GLOBAL_MULT, AttributeOperation.Add, 1), Is.True);
            ScoreRound(simulation, physics, FirstTarget);
            int firstProductionRoundOfNextDay = context.Round.RoundId + 2;
            DeviceRoundState.Write(context, original, firstProductionRoundOfNextDay,
                new Dictionary<string, double> { { "fixture-upcoming", 27 } });
            context.DeviceScripts.SetStatus(id, firstProductionRoundOfNextDay, "fixture status");
            Assert.That(simulation.TryCompleteRest(CompleteSettlement(simulation), out _), Is.True);
            Assert.That(simulation.GetOwnedDevices(), Has.Length.EqualTo(2));
            Assert.That(Array.TrueForAll(simulation.GetOwnedDevices(), device => !device.Placed), Is.True);
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
            Assert.That(context.DeviceScripts.Status(id, DeviceRoundState.Round(context)), Is.EqualTo("fixture status"));
            Assert.That(simulation.GetOwnedDevices()[0].Durability, Is.EqualTo(7));
            Assert.That(simulation.GetOwnedDevices()[0].Placed, Is.False);
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

        private static string Json(string days, string events = "[]", string rules = "[]", string campaignRules = "[]") =>
            $"{{\"schemaVersion\":2,\"sequenceId\":\"fixture\",\"days\":[{days}]," +
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

        private sealed class PhysicsStub : IMarbleDayPhysics, IMarbleDayPhysicsCheckpoint, IMarbleBoardLayoutPhysics
        {
            private readonly Dictionary<MarbleKey, Vector3> live = new Dictionary<MarbleKey, Vector3>();
            internal MarbleKey Last { get; private set; }
            internal BoardLayoutDefinition CurrentLayout { get; private set; }
            internal int LayoutChanges { get; private set; }
            public void ValidateBoardLayout(BoardLayoutDefinition layout) => BoardPlacementSurface.DefaultLayoutProjection.Validate(layout);
            public void ApplyBoardLayout(BoardLayoutDefinition layout) { CurrentLayout = layout; LayoutChanges++; }
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
