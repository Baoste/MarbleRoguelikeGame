using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarblesECS.PhysX;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MarblesECS.Tests
{
    public sealed class DaySceneTests
    {
        private Scene testScene, localScene;
        private GameObject machine, board;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (machine) Object.Destroy(machine);
            yield return null;
            if (board) Object.Destroy(board);
            if (testScene.IsValid() && testScene.isLoaded) yield return SceneManager.UnloadSceneAsync(testScene);
            if (localScene.IsValid()) yield return Until(() => !localScene.isLoaded, "local physics scene unload");
        }

        [UnityTest]
        public IEnumerator TestSceneStillLaunchesScoresShopsAndRestartsTheLegacySession()
        {
            yield return SceneManager.LoadSceneAsync("TestScene", LoadSceneMode.Additive);
            testScene = SceneManager.GetSceneByName("TestScene");
            var game = testScene.GetRootGameObjects().Select(root => root.GetComponentInChildren<MarbleGameController>())
                .First(controller => controller != null);
            yield return Until(() => game.IsReady, "TestScene controller startup");
            localScene = game.PhysicsRoot.gameObject.scene;
            Assert.That(game.Day.Status, Is.EqualTo(DayCampaignStatus.NotStarted));
            Assert.That(game.Snapshot.Phase, Is.EqualTo(RoundPhase.Playing));
            game.AutoMoveLauncher = false;
            var score = AddZone(game.PhysicsRoot, game.LaunchPoint.TransformPoint(game.LaunchOffset), game.Session.TargetScore);
            score.Priority = int.MaxValue;
            int contacts = 0;
            game.Scored += item => { if (item.ZoneId == score.TargetId) contacts++; };
            game.FireOnce();
            yield return Until(() => game.Session.CanCashOut, "legacy zone contact and score");
            Assert.That(contacts, Is.EqualTo(1));
            Assert.That(game.CashOut(), Is.True);
            yield return Until(() => game.Session.Phase == RoundPhase.Shop, "legacy round settlement and shop");
            Assert.That(game.GetShopOffers(), Is.Not.Empty);
            Assert.That(game.BuyOffer(0), Is.True);
            Assert.That(game.EnterBuild(), Is.True);
            Assert.That(game.GetOwnedDevices(), Is.Not.Empty);
            game.RestartRound();
            Assert.That(game.GetOwnedDevices(), Is.Empty);
            Assert.That(game.Snapshot.Phase, Is.EqualTo(RoundPhase.Playing));
        }

        [UnityTest]
        public IEnumerator DayFailureRebuildsRealPhysicsAndInventoryWhileKeepingCompletedDays()
        {
            var balance = GameBalanceLoader.Load();
            foreach (var device in balance.Devices) device.StartingCount = 0;
            var starter = balance.Devices.First(device => device.PurchaseQuantity == 1 && device.PlacementRadius > 0 &&
                !device.RotationLocked && device.StartingCount == 0);
            starter.StartingCount = 1;
            balance.Campaign.StartingCoins = 200;
            balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 2;
            balance.Marble.MinInvestment = balance.Marble.MaxInvestment = 1;
            balance.Marble.FireIntervalSeconds = balance.Marble.MaxFireIntervalSeconds = .01f;
            balance.Content.Definition(GameAttribute.BLOOD_COST).DefaultValue = 1;
            balance.Stages = new[]
            {
                new StageDefinition { Id = 10, TargetScore = 4 },
                new StageDefinition { Id = 20, TargetScore = 24 }
            };
            var worlds = OwnedWorlds();
            var game = CreateMachine(balance);
            var score = AddZone(board.transform, Vector3.zero, balance.Stages[0].TargetScore);
            yield return Until(() => game.IsReady, "fixture controller startup");
            localScene = board.scene;
            var world = OwnedWorlds().Single(candidate => !worlds.Contains(candidate));

            List<DayLifecycleNotice> log = new List<DayLifecycleNotice>();
            var behaviors = new DayBehaviorRegistry();
            behaviors.RegisterAction("record", raw => raw, (context, _, __) => { log.Add(context.Notice); return Task.CompletedTask; });
            var campaign = GameDaysLoader.Parse(FixtureJson, balance, behaviors);
            Assert.That(game.TryStartDayCampaign(campaign, out var error, 71), Is.True, error);
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            Assert.That(game.BeginRound(), Is.False);
            Assert.That(game.TryCompleteWakeUp(game.Day.Version, out _), Is.True);
            Assert.That(game.Snapshot.Phase, Is.EqualTo(RoundPhase.Build));
            int starterId = game.GetOwnedDevices().Single().InstanceId;
            Assert.That(game.PlaceDevice(starterId, 3, 2), Is.True);
            Assert.That(game.RotateDevice(starterId, 45), Is.True);
            yield return Until(() => DeviceBody(starterId) != null, "initial collision geometry");
            yield return PlayScoringRound(game);
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            FinishDay(game);
            Assert.That(game.Day.CompletedDays, Is.EqualTo(1));
            Assert.That(game.Day.DayId, Is.EqualTo("fixture-second"));
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            var checkpoint = game.Day;
            var savedDevice = game.GetOwnedDevices().Single();
            long savedCoins = game.Session.Coins;
            Assert.That(DeviceBody(starterId), Is.Not.Null);

            Assert.That(game.TryCompleteWakeUp(game.Day.Version, out _), Is.True);
            yield return PlayScoringRound(game);
            Assert.That(game.Session.Phase, Is.EqualTo(RoundPhase.Shop));
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.Production));
            Assert.That(game.Day.SettledRounds, Is.EqualTo(1));
            var offers = game.GetShopOffers();
            Assert.That(game.RefreshShop(), Is.True);
            Assert.That(game.BuyOffer(0), Is.True);
            Assert.That(game.GetShopOffers()[0].Sold, Is.True);
            Assert.That(game.EnterBuild(), Is.True);
            Assert.That(game.PlaceDevice(starterId, -3, 2), Is.True);
            Assert.That(game.RotateDevice(starterId, 45), Is.True);
            yield return Until(() => DeviceBody(starterId) != null, "moved collision geometry");
            Assert.That(game.BeginRound(), Is.True);
            yield return null;
            var attemptDevice = DeviceBody(starterId);
            Assert.That(attemptDevice, Is.Not.Null);
            Assert.That(attemptDevice.transform.localPosition.x, Is.EqualTo(-3).Within(.01f));
            score.Kind = MarbleContactKind.Drain;
            game.FireOnce();
            yield return Until(() => game.Snapshot.ActiveMarbles > 0, "first live Rigidbody during failed attempt");
            var attemptBall = localScene.GetRootGameObjects().Select(root => root.GetComponent<MarbleBody>())
                .First(body => body != null && body.isActiveAndEnabled);
            int failedRound = game.Snapshot.RoundId;
            yield return Until(() => game.Snapshot.ActiveMarbles == 0, "first drained marble");
            game.FireOnce();
            yield return Until(() => game.Snapshot.Phase == RoundPhase.Draining, "production draining");
            yield return Until(() => game.Day.Attempt == 2, "same Day retry");

            Assert.That(game.Day.DayId, Is.EqualTo(checkpoint.DayId));
            Assert.That(game.Day.CompletedDays, Is.EqualTo(1));
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            Assert.That(game.Day.RandomState, Is.EqualTo(checkpoint.RandomState));
            Assert.That(game.Session.TotalScore, Is.Zero);
            Assert.That(game.Session.Coins, Is.EqualTo(savedCoins));
            Assert.That(game.Session.ShopRefreshCount, Is.Zero);
            Assert.That(game.GetShopOffers(), Is.Empty);
            Assert.That(game.Snapshot.Blood, Is.EqualTo(2));
            Assert.That(game.Snapshot.RoundId, Is.GreaterThan(failedRound));
            Assert.That(game.Snapshot.ActiveMarbles, Is.Zero);
            Assert.That(attemptBall == null || !attemptBall.gameObject.activeInHierarchy, Is.True);
            Assert.That(attemptDevice == null || !attemptDevice.gameObject.activeInHierarchy, Is.True);
            var restored = game.GetOwnedDevices().Single();
            Assert.That(restored.InstanceId, Is.EqualTo(savedDevice.InstanceId));
            Assert.That(restored.PurchasePrice, Is.EqualTo(savedDevice.PurchasePrice));
            Assert.That(restored.RemainingDurability, Is.EqualTo(savedDevice.RemainingDurability));
            Assert.That(restored.X, Is.EqualTo(savedDevice.X));
            Assert.That(restored.Angle, Is.EqualTo(savedDevice.Angle));
            Assert.That(DeviceBody(starterId).transform.localPosition.x, Is.EqualTo(3).Within(.01f));
            Assert.That(log.Count(notice => notice.Trigger == DayTrigger.RoundSettled && notice.Day.Attempt == 1), Is.EqualTo(2));
            Assert.That(log.Count(notice => notice.Trigger == DayTrigger.DayRetried), Is.EqualTo(1));
            Assert.That(game.TryCompleteDaySettlement(game.Day.Version, out _), Is.False);
            game.RestartRound();
            Assert.That(game.Day.Attempt, Is.EqualTo(2));

            score.Kind = MarbleContactKind.Score;
            Assert.That(game.TryCompleteWakeUp(game.Day.Version, out _), Is.True);
            yield return PlayScoringRound(game);
            Assert.That(game.GetShopOffers().Select(offer => offer.DefinitionId),
                Is.EqualTo(offers.Select(offer => offer.DefinitionId)), "retry replays the same shop draws");
            Assert.That(game.EnterBuild(), Is.True);
            score.FlatScore = balance.Stages[1].TargetScore;
            yield return PlayScoringRound(game);
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            FinishDay(game);
            Assert.That(game.Day.Status, Is.EqualTo(DayCampaignStatus.Completed));
            Assert.That(game.Day.CompletedDays, Is.EqualTo(2));
            yield return null;
            Assert.That(attemptBall == null, Is.True, "retired pooled body should be destroyed");
            Assert.That(attemptDevice == null, Is.True, "retired device collider should be destroyed");

            Object.Destroy(machine);
            yield return Until(() => !world.IsCreated && !localScene.isLoaded, "day world and PhysX scene disposal");
            Object.Destroy(board);
            yield return null;
            game = CreateMachine(balance);
            yield return Until(() => game.IsReady, "new controller after Day teardown");
            localScene = board.scene;
            Assert.That(game.Day.Status, Is.EqualTo(DayCampaignStatus.NotStarted));
            Assert.That(game.Snapshot.ActiveMarbles, Is.Zero);
            Assert.That(OwnedWorlds().Count, Is.EqualTo(worlds.Count + 1));
            Assert.That(DeviceBody(starterId), Is.Null);
        }

        [UnityTest]
        public IEnumerator PhysicsCheckpointPreservesEarlierPinDamageAndRetiresBodiesAndPool()
        {
            localScene = SceneManager.CreateScene("Day checkpoint physics", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            testScene = localScene;
            using var bridge = new MarblePhysicsBridge(localScene, null, null, _ => { });
            var firstPin = AddPin(new Vector3(2, 0, 2));
            var secondPin = AddPin(new Vector3(2, 0, 0));
            var tool = new GameObject("Fixture device");
            SceneManager.MoveGameObjectToScene(tool, localScene);
            tool.AddComponent<MResDeviceBody>().Initialize(1, new DeviceDefinition(), null);
            Physics.SyncTransforms();
            bridge.BreakPinsOnCross(1);
            Assert.That(firstPin.enabled, Is.False);
            Assert.That(secondPin.enabled, Is.True);
            var checkpoint = bridge.CaptureDayCheckpoint();
            tool.transform.position = new Vector3(0, 0, 2);
            Physics.SyncTransforms();
            bridge.BreakPinsOnCross(1);
            Assert.That(secondPin.enabled, Is.False);

            var firstBody = new MarbleSpawnData
            {
                Key = new MarbleKey(1, 1), Position = new Vector3(0, 3, 0), Rotation = Quaternion.identity,
                Quote = new ShotQuote(1, 1, .1f, 1, 1, 1)
            };
            var secondBody = firstBody;
            secondBody.Key = new MarbleKey(1, 2);
            secondBody.Position += Vector3.right;
            Assert.That(bridge.TrySpawn(firstBody), Is.True);
            Assert.That(bridge.TrySpawn(secondBody), Is.True);
            bridge.Remove(secondBody.Key);
            Assert.That(bridge.PooledBodyCount, Is.EqualTo(1));
            Assert.That(bridge.ActiveBodyCount, Is.EqualTo(1));

            checkpoint.Restore();
            Assert.That(firstPin.enabled, Is.False);
            Assert.That(secondPin.enabled, Is.True);
            Assert.That(bridge.ActiveBodyCount, Is.Zero);
            Assert.That(bridge.PooledBodyCount, Is.Zero);
            yield return null;
            Assert.That(localScene.GetRootGameObjects().Select(root => root.GetComponent<MarbleBody>()).Any(body => body != null), Is.False);
        }

        private Collider AddPin(Vector3 position)
        {
            var pin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(pin, localScene);
            pin.transform.position = position;
            pin.transform.localScale = Vector3.one * .3f;
            pin.AddComponent<MarblePin>().ScoreMultiplier = 1;
            return pin.GetComponent<Collider>();
        }

        private MarbleGameController CreateMachine(GameBalance balance)
        {
            board = new GameObject("Day test board");
            machine = new GameObject("Day test controller");
            var launcher = new GameObject("Day test launcher").transform;
            launcher.SetParent(board.transform, false);
            launcher.localPosition = new Vector3(0, 3, 0);
            var game = machine.AddComponent<MarbleGameController>();
            game.PhysicsRoot = board.transform;
            game.LaunchPoint = launcher;
            game.AutoMoveLauncher = false;
            game.RefreshRandomSeedOnNewRun = false;
            game.ConfigureCampaign(balance);
            var placement = machine.AddComponent<DevicePlacementUI>();
            placement.Controller = game;
            placement.BoardRoot = board.transform;
            return game;
        }

        private static MarbleZone AddZone(Transform root, Vector3 worldPosition, long flatScore)
        {
            var node = new GameObject("Day test score zone");
            SceneManager.MoveGameObjectToScene(node, root.gameObject.scene);
            node.transform.SetParent(root, false);
            node.transform.position = worldPosition;
            var collider = node.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(1, .15f, 1);
            var zone = node.AddComponent<MarbleZone>();
            zone.TargetId = 99999;
            zone.FlatScore = flatScore;
            zone.RushEnabled = false;
            zone.ScoreStackEnabled = false;
            return zone;
        }

        private MResDeviceBody DeviceBody(int id) =>
            MResDeviceBody.Active.FirstOrDefault(item => item != null && item.InstanceId == id && item.gameObject.scene == localScene);

        private static List<World> OwnedWorlds()
        {
            var worlds = new List<World>();
            foreach (var world in World.All)
                if (world.Name == "MarblesECS.OwnedGameplayWorld") worlds.Add(world);
            return worlds;
        }

        private static IEnumerator PlayScoringRound(MarbleGameController game)
        {
            Assert.That(game.BeginRound(), Is.True);
            game.FireOnce();
            yield return Until(() => game.Session.CanCashOut, "real PhysX score");
            Assert.That(game.CashOut(), Is.True);
            yield return Until(() => game.Snapshot.Phase != RoundPhase.Draining, "round settlement");
        }

        private static void FinishDay(MarbleGameController game)
        {
            Assert.That(game.TryCompleteDaySettlement(game.Day.Version, out var error), Is.True, error);
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.Rest));
            Assert.That(game.TryCompleteRest(game.Day.Version, out error), Is.True, error);
        }

        private static IEnumerator Until(Func<bool> condition, string operation)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, operation);
        }

        private const string FixtureJson = "{\"schemaVersion\":1,\"sequenceId\":\"fixture\"," +
            "\"days\":[{\"dayId\":\"fixture-first\",\"order\":1,\"productionPlanId\":\"single\"}," +
            "{\"dayId\":\"fixture-second\",\"order\":2,\"productionPlanId\":\"multiple\",\"eventIds\":[\"round\",\"retry\"]}]," +
            "\"productionPlans\":[{\"planId\":\"single\",\"firstStageId\":10,\"lastStageId\":10}," +
            "{\"planId\":\"multiple\",\"firstStageId\":10,\"lastStageId\":20}]," +
            "\"events\":[{\"eventId\":\"round\",\"trigger\":\"RoundSettled\",\"executionPolicy\":\"EveryOccurrence\",\"actions\":[{\"type\":\"record\"}]}," +
            "{\"eventId\":\"retry\",\"trigger\":\"DayRetried\",\"actions\":[{\"type\":\"record\"}]}]}";
    }
}
