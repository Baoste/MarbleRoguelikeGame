using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cinemachine;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using NUnit.Framework;
using TMPro;
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
        public IEnumerator TestSceneLaunchesScoresAdvancesAndRestartsTheMachineSession()
        {
            yield return LoadTestScene(controller =>
            {
                controller.StartWithDayCampaign = false;
                controller.EnableRandomStartingDevices = false;
            });
            var game = InTestScene<MarbleGameController>();
            yield return Until(() => game.IsReady, "TestScene controller startup");
            localScene = game.PhysicsRoot.gameObject.scene;
            Assert.That(game.Day.Status, Is.EqualTo(DayCampaignStatus.NotStarted));
            Assert.That(game.Snapshot.Phase, Is.EqualTo(RoundPhase.Playing));
            Assert.That(game.Session.TargetScore, Is.EqualTo(game.Balance.Marble.TargetScore));
            game.AutoMoveLauncher = false;
            var score = AddZone(game.PhysicsRoot, game.LaunchPoint.TransformPoint(game.LaunchOffset), game.Session.TargetScore);
            score.Priority = int.MaxValue;
            int contacts = 0;
            game.Scored += item => { if (item.ZoneId == score.TargetId) contacts++; };
            game.FireOnce();
            yield return Until(() => game.Session.CanCashOut, "legacy zone contact and score");
            Assert.That(contacts, Is.EqualTo(1));
            Assert.That(game.CashOut(), Is.True);
            yield return Until(() => game.Session.Phase == RoundPhase.Build, "the next build follows settlement");
            Assert.That(game.GetShopOffers(), Is.Empty);
            Assert.That(game.ShopOpen, Is.False);
            Assert.That(game.Session.TargetScore, Is.EqualTo(game.Balance.Marble.TargetScore));
            Assert.That(game.BeginRound(), Is.True);
            Assert.That(game.Session.CanCashOut, Is.False, "each machine round must reach its own target");
            game.RestartRound();
            Assert.That(game.GetOwnedDevices(), Is.Empty);
            Assert.That(game.Snapshot.Phase, Is.EqualTo(RoundPhase.Playing));
        }

        [UnityTest]
        public IEnumerator StartingDevicesLaunchTheFullMachineCampaign()
        {
            yield return LoadTestScene(controller => controller.EnableRandomStartingDevices = true);
            var game = InTestScene<MarbleGameController>();
            yield return Until(() => game.IsReady, "full machine startup");
            localScene = game.PhysicsRoot.gameObject.scene;
            Assert.That(game.StartWithDayCampaign, Is.True);
            Assert.That(game.Day.Status, Is.EqualTo(DayCampaignStatus.NotStarted));
            Assert.That(game.Session.Phase, Is.EqualTo(RoundPhase.Build));
            Assert.That(game.HasProductionFeature(ProductionFeature.All), Is.True);
            Assert.That(game.GetOwnedDevices().Select(device => device.DefinitionId).Distinct(),
                Is.EquivalentTo(game.Balance.Devices.Select(device => device.Id)));
            Assert.That(InTestScene<MorningPrepController>().transform.Find("Morning Prep Overlay"), Is.Null);

            var brain = InTestScene<CinemachineBrain>();
            var rig = InTestScene<DiegeticMachineUI>();
            yield return Until(() => brain.ActiveVirtualCamera?.Name == "VCamera_Machine" && rig.IsBoardOpen,
                "machine camera and build controls are active");
            Assert.That(rig.TryContinue(), Is.True);
            var score = InTestScene<ScoreListener>();
            var wheel = InTestScene<GamblingWheelDisplay>();
            yield return Until(() => game.Session.Phase == RoundPhase.Playing &&
                score.RushText.enabled && wheel.DisplayGroup.alpha > 0, "RUSH and the wheel are visible during play");
        }

        [UnityTest]
        public IEnumerator ConfiguredDaysRequireTheirNotesAndEnableTheDayOneShop()
        {
            yield return SceneManager.LoadSceneAsync("TestScene", LoadSceneMode.Additive);
            testScene = SceneManager.GetSceneByName("TestScene");
            var game = InTestScene<MarbleGameController>();
            var prep = InTestScene<MorningPrepController>();
            var walker = InTestScene<RoomCameraMovement>();
            var brain = InTestScene<CinemachineBrain>();
            yield return Until(() => game.IsReady, "Day0 startup");
            localScene = game.PhysicsRoot.gameObject.scene;
            Assert.That(game.Day.DayId, Is.EqualTo("day0"));
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            Assert.That(game.Session.TargetScore, Is.EqualTo(50));
            var pins = game.PhysicsRoot.GetComponentsInChildren<MarblePin>();
            var scoreZone = game.PhysicsRoot.GetComponentsInChildren<MarbleZone>().Single();
            Assert.That(pins, Has.Length.EqualTo(3));
            Assert.That(pins.All(pin => pin.ScoreMultiplier == 2 && pin.RushChanceAdd == 0), Is.True);
            Assert.That(pins[0].gameObject.scene, Is.EqualTo(localScene));
            Assert.That(scoreZone.Kind, Is.EqualTo(MarbleContactKind.Score));
            Assert.That(scoreZone.GetComponent<BoxCollider>().isTrigger, Is.True);
            Assert.That(scoreZone.transform.parent, Is.EqualTo(pins[0].transform.parent));
            Assert.That(pins[0].transform.localPosition.z, Is.GreaterThan(0), "the first pin is left of center");
            Assert.That(pins[1].transform.localPosition.z, Is.LessThan(0), "the second pin is right of center");
            Assert.That(game.TryOpenShop(game.Day.Version), Is.False);
            Assert.That(game.BeginRound(), Is.False);
            Assert.That(prep.FadeAlpha, Is.GreaterThan(0), "wake-up starts black");
            Assert.That(prep.TryInteract(), Is.False);
            var origin = walker.transform.position;
            walker.Move(Vector2.up, .25f);
            Assert.That(walker.transform.position, Is.EqualTo(origin));

            yield return Until(() => walker.CanMove, "fade finished and room camera is active");
            Assert.That(brain.ActiveVirtualCamera.Name, Is.EqualTo("VCamera_Free"));
            var shopAnchor = prep.transform.Find("Shop Terminal Anchor");
            Assert.That(shopAnchor.childCount, Is.EqualTo(1), "the disabled shop has only its scene interaction point");
            var noteAnchor = prep.transform.Find("Platform Note Anchor");
            Assert.That(noteAnchor.Find("Morning Prep Note"), Is.Not.Null);
            var notePoint = noteAnchor.Find("Platform Note Interaction").position;
            var machinePoint = prep.transform.Find("Marble Machine Interaction").position;
            Assert.That(Vector3.Distance(origin, machinePoint), Is.LessThan(10f));
            var wakeCamera = brain.GetComponent<Camera>();
            foreach (var point in new[] { game.LaunchPoint.position, noteAnchor.position })
            {
                var viewport = wakeCamera.WorldToViewportPoint(point);
                Assert.That(viewport.z, Is.Positive);
                Assert.That(viewport.x, Is.InRange(0f, 1f));
                Assert.That(viewport.y, Is.InRange(0f, 1f));
            }
            walker.Move(Vector2.up, .1f);
            Assert.That(walker.transform.position.x, Is.GreaterThan(origin.x));
            Assert.That(walker.transform.position.y, Is.EqualTo(origin.y).Within(.05f));
            WalkTo(walker, -14, -13);
            WalkTo(walker, -14, -2);
            Assert.That(prep.CanUseMachine, Is.False);
            Assert.That(prep.TryInteract(), Is.False, "E at the machine is blocked while the note is unread");
            Assert.That(game.TryCompleteWakeUp(game.Day.Version, out _), Is.False);

            WalkTo(walker, -14, -13);
            WalkTo(walker, notePoint.x - 5, -13);
            WalkTo(walker, notePoint.x - 5, notePoint.z);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(prep.IsReading, Is.True);
            Assert.That(walker.CanMove, Is.False);
            Assert.That(game.Day.MorningPrepRemaining, Is.EqualTo(1));
            var displayedNote = prep.GetComponentsInChildren<TextMeshProUGUI>()
                .Single(label => label.name == "Note Text");
            Assert.That(displayedNote.text, Is.EqualTo(game.CurrentDayDefinition.MorningPrep.Single().Text));
            string onboardingCharacters = string.Concat(displayedNote.text.Where(character => !char.IsWhiteSpace(character)));
            Assert.That(displayedNote.font.HasCharacters(onboardingCharacters, out uint[] _, true, true), Is.True);
            Canvas.ForceUpdateCanvases();
            displayedNote.ForceMeshUpdate();
            Assert.That(displayedNote.isTextOverflowing, Is.False, "the onboarding note fits inside the note card");
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.MorningPrepRemaining, Is.Zero);

            yield return Until(() => walker.CanMove, "reading unlocks room movement");
            WalkTo(walker, notePoint.x - 5, -13);
            WalkTo(walker, -14, -13);
            WalkTo(walker, -14, -2);
            Assert.That(prep.CanUseMachine, Is.True);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp), "production waits for the priority blend");
            Assert.That(walker.CanMove, Is.False);
            yield return Until(() => game.Day.Phase == DayPhase.Production && game.Snapshot.Phase == RoundPhase.Playing,
                "machine camera takes over and opens basic production");
            Assert.That(brain.ActiveVirtualCamera.Name, Is.EqualTo("VCamera_Machine"));
            Assert.That(brain.IsBlending, Is.False);
            Assert.That(walker.CanMove, Is.False);
            Assert.That(game.HasProductionFeature(ProductionFeature.Shop), Is.False);
            Assert.That(game.HasProductionFeature(ProductionFeature.DevicePlacement), Is.False);
            Assert.That(game.HasProductionFeature(ProductionFeature.Rush), Is.False);
            Assert.That(game.HasProductionFeature(ProductionFeature.GamblingWheel), Is.False);
            Assert.That(game.GetShopOffers(), Is.Empty);
            Assert.That(InTestScene<DiegeticMachineUI>().IsBoardOpen, Is.False);
            var library = InTestScene<ItemLibraryUI>();
            library.Open();
            Assert.That(library.IsOpen, Is.False);
            var status = InTestScene<ScoreListener>();
            Assert.That(status.RushText.enabled, Is.False);
            Assert.That(status.RushSlider.gameObject.activeSelf, Is.False);
            Assert.That(InTestScene<GamblingWheelDisplay>().DisplayGroup.alpha, Is.Zero);

            game.AutoMoveLauncher = false;
            game.SetLauncherPosition(game.LauncherCenterZ);
            game.SetFlow(1f);
            int zoneHits = 0;
            game.Scored += item => { if (item.ZoneId == scoreZone.TargetId && !item.IsPending) zoneHits++; };
            game.SetFireHeld(true);
            float deadline = Time.realtimeSinceStartup + 15;
            while (!game.Session.CanCashOut && Time.realtimeSinceStartup < deadline) yield return null;
            game.SetFireHeld(false);
            Assert.That(zoneHits, Is.GreaterThan(0));
            Assert.That(game.Session.CanCashOut, Is.True,
                $"Day0 target after {zoneHits} Score zone hits, with {game.Snapshot.Blood} blood and {game.Snapshot.ActiveMarbles} marbles remaining");
            Assert.That(game.GamblingWheel.BallCount, Is.Zero);
            Assert.That(game.CashOut(), Is.True);
            yield return Until(() => game.Day.Phase == DayPhase.DaySettlement, "Day0 settlement");
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.DayId, Is.EqualTo("day1"));
            Assert.That(game.Session.TargetScore, Is.EqualTo(200));
            Assert.That(game.HasProductionFeature(ProductionFeature.Shop | ProductionFeature.DevicePlacement), Is.True);
            yield return Until(() => walker.CanMove, "Day1 Morning Prep begins");
            Assert.That(shopAnchor.childCount, Is.EqualTo(2), "Day1 spawns the shop terminal");
            Assert.That(game.Day.MorningPrepRemaining, Is.EqualTo(1));

            WalkTo(walker, notePoint.x - 5, -13);
            WalkTo(walker, notePoint.x - 5, notePoint.z);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(prep.IsReading, Is.True);
            Assert.That(displayedNote.text, Is.EqualTo(game.CurrentDayDefinition.MorningPrep.Single().Text));
            Assert.That(displayedNote.text, Does.Contain("200 分"));
            string printedCharacters = string.Concat(displayedNote.text.Where(character => !char.IsWhiteSpace(character)));
            Assert.That(displayedNote.font.HasCharacters(printedCharacters, out uint[] missing, true, true), Is.True,
                "Missing note characters: " + string.Join(", ", missing ?? Array.Empty<uint>()));
            Canvas.ForceUpdateCanvases();
            displayedNote.ForceMeshUpdate();
            Assert.That(displayedNote.isTextOverflowing, Is.False, "the whole announcement fits inside the note card");
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => walker.CanMove, "finished reading the Day1 announcement");

            var terminal = shopAnchor.Find("Shop Terminal Interaction");
            var shop = InTestScene<ShopUI>();
            WalkTo(walker, notePoint.x - 5, -13);
            WalkTo(walker, terminal.position.x - 5, -13);
            WalkTo(walker, terminal.position.x - 5, terminal.position.z);
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => shop.ShopCanvasGroup.interactable, "Day1 shop opens at the terminal");
            var offers = game.GetShopOffers();
            Assert.That(offers, Is.Not.Empty);
            Assert.That(game.BuyOffer(offers[0].Index), Is.True);
            Assert.That(game.GetOwnedDevices().Any(device => device.DefinitionId == offers[0].DefinitionId), Is.True);
            shop.ContinueButton.onClick.Invoke();
            yield return Until(() => walker.CanMove, "Day1 shopping returns to the room");

            WalkTo(walker, terminal.position.x - 5, -13);
            WalkTo(walker, -14, -13);
            WalkTo(walker, -14, -2);
            Assert.That(prep.TryInteract(), Is.True);
            var rig = InTestScene<DiegeticMachineUI>();
            yield return Until(() => game.Day.Phase == DayPhase.Production && rig.IsBoardOpen, "Day1 opens device placement");
            Assert.That(game.Snapshot.Phase, Is.EqualTo(RoundPhase.Build));
            Assert.That(rig.TryContinue(), Is.True);
            game.SetLauncherPosition(game.LauncherCenterZ);
            game.SetFlow(1f);
            game.SetFireHeld(true);
            yield return Until(() => game.Session.CanCashOut || game.Day.Phase == DayPhase.DaySettlement,
                "Day1 reaches its 200-point target with the existing board");
            game.SetFireHeld(false);
            if (game.Session.CanCashOut) Assert.That(game.CashOut(), Is.True);
            yield return Until(() => game.Day.Phase == DayPhase.DaySettlement, "Day1 settlement");
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.Status, Is.EqualTo(DayCampaignStatus.Completed));
        }

        [UnityTest]
        public IEnumerator BoardLayoutSwapsTargetsAndRestoresTheAuthoredBoard()
        {
            localScene = SceneManager.CreateScene("Day layouts physics", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            testScene = localScene;
            board = new GameObject("Fixture Board");
            SceneManager.MoveGameObjectToScene(board, localScene);
            var surface = board.AddComponent<BoardPlacementSurface>();
            var originalScore = AddZone(board.transform, Vector3.zero, 4);
            var drain = AddZone(board.transform, Vector3.left * 5, 0);
            drain.Kind = MarbleContactKind.Drain;
            drain.TargetId = 4;
            using var bridge = new MarblePhysicsBridge(localScene, null, null, _ => { });
            bridge.ConfigureBoardLayout(board.transform, surface);
            var day0 = BoardLayoutLoader.Load("Day0.json");
            bridge.ApplyBoardLayout(day0);
            var first = bridge.GeneratedBoardLayout;
            CheckFirstBoard();
            bridge.CaptureDayCheckpoint().Restore();
            CheckFirstBoard();

            var next = BoardLayoutLoader.Parse("{\"schemaVersion\":1,\"pins\":[],\"zones\":[" +
                "{\"type\":\"RandomScore\",\"x\":0.5,\"y\":0.75,\"width\":2,\"depth\":0.5,\"priority\":7,\"multiplier\":3}]}");
            bridge.ApplyBoardLayout(next);
            Assert.That(first.gameObject.activeSelf, Is.False);
            var generated = bridge.GeneratedBoardLayout.GetComponentInChildren<MarbleZone>();
            Assert.That(generated.Kind, Is.EqualTo(MarbleContactKind.RandomScore));
            Assert.That(generated.Priority, Is.EqualTo(7));
            Assert.That(generated.Multiplier, Is.EqualTo(3));
            bridge.ApplyBoardLayout(null);
            Assert.That(bridge.GeneratedBoardLayout, Is.Null);
            Assert.That(originalScore.isActiveAndEnabled && drain.isActiveAndEnabled, Is.True);
            yield return null;
            Assert.That(first == null && generated == null, Is.True);

            void CheckFirstBoard()
            {
                Assert.That(bridge.GeneratedBoardLayout, Is.SameAs(first));
                Assert.That(originalScore.isActiveAndEnabled, Is.False);
                Assert.That(drain.isActiveAndEnabled, Is.True);
                var pin = first.GetComponentInChildren<MarblePin>();
                var obstacles = new DevicePlacementObstacles();
                Assert.That(obstacles.IsClear(localScene.GetPhysicsScene(), null, pin.transform.position, .08f, first), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator EnabledMorningShopOpensAtTheTerminalAndReturnsToTheRoom()
        {
            var balance = GameBalanceLoader.Load();
            balance.Campaign.StartingCoins = 100;
            yield return LoadTestScene(controller =>
            {
                controller.StartWithDayCampaign = false;
                controller.EnableRandomStartingDevices = false;
                controller.ConfigureCampaign(balance);
            });
            var game = InTestScene<MarbleGameController>();
            var prep = InTestScene<MorningPrepController>();
            var walker = InTestScene<RoomCameraMovement>();
            var brain = InTestScene<CinemachineBrain>();
            var shop = InTestScene<ShopUI>();
            yield return Until(() => game.IsReady, "shop fixture startup");
            localScene = game.PhysicsRoot.gameObject.scene;
            var terminalAnchor = prep.transform.Find("Shop Terminal Anchor");
            Assert.That(terminalAnchor.childCount, Is.EqualTo(1));
            var definition = GameDaysLoader.Parse(@"{""schemaVersion"":2,""sequenceId"":""shop-test"",""days"":[
                {""dayId"":""fixture-shop"",""order"":1,""targetScore"":50,""rewardCoins"":0,""productionFeatures"":[""Shop""],
                ""morningPrep"":[{""objectiveId"":""read-note"",""type"":""ReadNote"",""anchorId"":""desk"",""text"":""fixture note""}]},
                {""dayId"":""fixture-closed"",""order"":2,""targetScore"":50,""rewardCoins"":0}]}", balance);
            Assert.That(game.TryStartDayCampaign(definition, out string error, 71), Is.True, error);
            yield return Until(() => walker.CanMove, "room fades in");
            Assert.That(prep.TryInteract(), Is.False, "the terminal requires proximity");
            Assert.That(game.GetShopOffers(), Is.Empty);
            Assert.That(terminalAnchor.childCount, Is.EqualTo(2), "the enabled day spawns one terminal prefab");
            Assert.That(terminalAnchor.GetComponentInChildren<TextMeshPro>(), Is.Not.Null);
            var wakePosition = walker.transform.position;

            var terminal = terminalAnchor.Find("Shop Terminal Interaction");
            var notePoint = prep.transform.Find("Platform Note Anchor/Platform Note Interaction").position;
            WalkTo(walker, terminal.position.x - 5, -13);
            WalkTo(walker, terminal.position.x - 5, terminal.position.z);
            var roomPosition = walker.transform.position;
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => shop.ShopCanvasGroup.interactable, "existing shop canvas opens");
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            Assert.That(game.Day.MorningPrepRemaining, Is.EqualTo(1), "the nearer terminal opens while the note is unread");
            Assert.That(brain.ActiveVirtualCamera.Name, Is.EqualTo("VCamera_Free"));
            Assert.That(walker.CanMove, Is.False);
            Assert.That(game.TryCompleteWakeUp(game.Day.Version, out _), Is.False);
            var stock = game.GetShopOffers();
            Assert.That(stock, Is.Not.Empty);
            var firstCard = shop.OfferContainer.GetComponentsInChildren<ShopOfferItemUI>().First();
            Assert.That(firstCard.BuyButton.IsInteractable(), Is.True);
            long coins = game.Session.Coins;
            firstCard.BuyButton.onClick.Invoke();
            Assert.That(game.GetShopOffers()[0].Sold, Is.True);
            Assert.That(game.GetOwnedDevices().Any(device => device.DefinitionId == stock[0].DefinitionId), Is.True);
            Assert.That(game.Session.Coins, Is.EqualTo(coins - stock[0].Price));
            uint afterPurchase = game.Day.RandomState;

            shop.ContinueButton.onClick.Invoke();
            yield return Until(() => walker.CanMove && !shop.ShopCanvasGroup.blocksRaycasts, "Return to room closes the shop");
            Assert.That(walker.transform.position, Is.EqualTo(roomPosition));
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            Assert.That(game.BuyOffer(1), Is.False);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.RandomState, Is.EqualTo(afterPurchase));
            Assert.That(game.GetShopOffers()[0].Sold, Is.True);
            Assert.That(prep.TryInteract(), Is.True, "E also closes the shop");

            yield return Until(() => walker.CanMove, "walking resumes");
            WalkTo(walker, notePoint.x - 5, notePoint.z);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(prep.IsReading, Is.True, "the nearer paper is selected while the terminal is also in range");
            Assert.That(game.ShopOpen, Is.False);
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => walker.CanMove, "closing the note resumes walking");
            WalkTo(walker, notePoint.x - 5, -13);
            WalkTo(walker, -14, -13);
            WalkTo(walker, -14, -2);
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => game.Day.Phase == DayPhase.Production && game.Session.Phase == RoundPhase.Playing,
                "machine blend completes");
            Assert.That(brain.ActiveVirtualCamera.Name, Is.EqualTo("VCamera_Machine"));
            Assert.That(game.TryOpenShop(game.Day.Version), Is.False);
            Assert.That(shop.ShopCanvasGroup.alpha, Is.Zero);
            game.AutoMoveLauncher = false;
            var score = AddZone(game.PhysicsRoot, game.LaunchPoint.TransformPoint(game.LaunchOffset), game.Session.TargetScore);
            score.Priority = int.MaxValue;
            game.FireOnce();
            yield return Until(() => game.Session.CanCashOut, "physical score");
            Assert.That(game.CashOut(), Is.True);
            yield return Until(() => game.Day.Phase == DayPhase.DaySettlement, "shop day settlement");
            FinishDay(game);
            Assert.That(game.Day.DayId, Is.EqualTo("fixture-closed"));
            yield return Until(() => terminalAnchor.childCount == 1, "next day removes the disabled shop prefab");
            Assert.That(terminalAnchor.GetComponentInChildren<TextMeshPro>(true), Is.Null);
            Assert.That(walker.transform.position, Is.EqualTo(wakePosition));
        }

        private IEnumerator LoadTestScene(Action<MarbleGameController> configure)
        {
            void OnLoaded(Scene scene, LoadSceneMode mode)
            {
                if (scene.name != "TestScene") return;
                testScene = scene;
                configure(InTestScene<MarbleGameController>());
            }

            SceneManager.sceneLoaded += OnLoaded;
            try { yield return SceneManager.LoadSceneAsync("TestScene", LoadSceneMode.Additive); }
            finally { SceneManager.sceneLoaded -= OnLoaded; }
        }

        private T InTestScene<T>() where T : Component =>
            testScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).First();

        private static void WalkTo(RoomCameraMovement walker, float x, float z)
        {
            var target = new Vector3(x, walker.transform.position.y, z);
            var heading = Quaternion.Euler(0f, walker.transform.eulerAngles.y, 0f);
            for (int i = 0; i < 100; i++)
            {
                var displacement = target - walker.transform.position;
                displacement.y = 0;
                if (displacement.sqrMagnitude < .25f) return;
                var local = Quaternion.Inverse(heading) * displacement;
                walker.Move(new Vector2(local.x, local.z).normalized, .06f);
            }
            Assert.Fail($"Room walker did not reach {target}. It stopped at {walker.transform.position}.");
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
            var worlds = OwnedWorlds();
            var game = CreateMachine(balance);
            var score = AddZone(board.transform, Vector3.zero, 4);
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
            yield return Until(() => FindDeviceBody(starterId) != null, "initial collision geometry");
            yield return PlayScoringRound(game);
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            var placedDevice = game.GetOwnedDevices().Single();
            Assert.That(placedDevice.Placed, Is.True);
            FinishDay(game);
            Assert.That(game.Day.CompletedDays, Is.EqualTo(1));
            Assert.That(game.Day.DayId, Is.EqualTo("fixture-second"));
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            var checkpoint = game.Day;
            var savedDevice = game.GetOwnedDevices().Single();
            long savedCoins = game.Session.Coins;
            Assert.That(savedDevice.InstanceId, Is.EqualTo(placedDevice.InstanceId));
            Assert.That(savedDevice.PurchasePrice, Is.EqualTo(placedDevice.PurchasePrice));
            Assert.That(savedDevice.Placed, Is.False, "the next morning starts with the same device in the backpack");
            Assert.That(FindDeviceBody(starterId), Is.Null);

            Assert.That(game.TryOpenShop(game.Day.Version), Is.True);
            Assert.That(log.Single(notice => notice.Trigger == DayTrigger.ShopEntered).Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            var offers = game.GetShopOffers();
            Assert.That(game.RefreshShop(), Is.True);
            Assert.That(game.BuyOffer(0), Is.True);
            Assert.That(game.GetShopOffers()[0].Sold, Is.True);
            Assert.That(game.TryCloseShop(game.Day.Version), Is.True);
            Assert.That(game.TryCompleteWakeUp(game.Day.Version, out _), Is.True);
            Assert.That(game.Session.Phase, Is.EqualTo(RoundPhase.Build));
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.Production));
            Assert.That(game.Day.SettledRounds, Is.Zero);
            Assert.That(game.ShopOpen || game.RefreshShop(), Is.False);
            Assert.That(game.PlaceDevice(starterId, -3, 2), Is.True);
            Assert.That(game.RotateDevice(starterId, 45), Is.True);
            yield return Until(() => FindDeviceBody(starterId) != null, "moved collision geometry");
            Assert.That(game.BeginRound(), Is.True);
            yield return null;
            var attemptDevice = FindDeviceBody(starterId);
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
            Assert.That(restored.Placed, Is.False, "retry restores the morning backpack");
            Assert.That(FindDeviceBody(starterId), Is.Null);
            Assert.That(log.Count(notice => notice.Trigger == DayTrigger.RoundSettled && notice.Day.Attempt == 1), Is.EqualTo(1));
            Assert.That(log.Count(notice => notice.Trigger == DayTrigger.DayRetried), Is.EqualTo(1));
            Assert.That(game.TryCompleteDaySettlement(game.Day.Version, out _), Is.False);
            game.RestartRound();
            Assert.That(game.Day.Attempt, Is.EqualTo(2));

            score.Kind = MarbleContactKind.Score;
            Assert.That(game.TryOpenShop(game.Day.Version), Is.True);
            Assert.That(game.GetShopOffers().Select(offer => offer.DefinitionId),
                Is.EqualTo(offers.Select(offer => offer.DefinitionId)), "retry replays the same shop draws");
            Assert.That(game.TryCloseShop(game.Day.Version), Is.True);
            Assert.That(game.TryCompleteWakeUp(game.Day.Version, out _), Is.True);
            Assert.That(game.PlaceDevice(starterId, 3, 2), Is.True);
            yield return Until(() => FindDeviceBody(starterId) != null, "device can be placed again from the backpack");
            score.FlatScore = campaign.Days[1].TargetScore;
            yield return PlayScoringRound(game);
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.DaySettlement));
            FinishDay(game);
            Assert.That(game.Day.Status, Is.EqualTo(DayCampaignStatus.Completed));
            Assert.That(game.Day.CompletedDays, Is.EqualTo(2));
            Assert.That(game.GetOwnedDevices().Single().Placed, Is.False, "the final day also returns the device");
            yield return Until(() => FindDeviceBody(starterId) == null, "the final device collider is withdrawn");
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
            Assert.That(FindDeviceBody(starterId), Is.Null);
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
            tool.AddComponent<DeviceBody>().Initialize(1, new DeviceDefinition(), null);
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

        private DeviceBody FindDeviceBody(int id) =>
            DeviceBody.Active.FirstOrDefault(item => item != null && item.InstanceId == id && item.gameObject.scene == localScene);

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

        private const string FixtureJson = "{\"schemaVersion\":2,\"sequenceId\":\"fixture\"," +
            "\"days\":[{\"dayId\":\"fixture-first\",\"order\":1,\"targetScore\":4,\"rewardCoins\":35,\"productionFeatures\":[\"Shop\",\"DevicePlacement\",\"Rush\",\"GamblingWheel\"]}," +
            "{\"dayId\":\"fixture-second\",\"order\":2,\"targetScore\":24,\"rewardCoins\":45,\"eventIds\":[\"round\",\"retry\",\"shop\"]," +
            "\"productionFeatures\":[\"Shop\",\"DevicePlacement\",\"Rush\",\"GamblingWheel\"]}]," +
            "\"events\":[{\"eventId\":\"round\",\"trigger\":\"RoundSettled\",\"executionPolicy\":\"EveryOccurrence\",\"actions\":[{\"type\":\"record\"}]}," +
            "{\"eventId\":\"retry\",\"trigger\":\"DayRetried\",\"actions\":[{\"type\":\"record\"}]}," +
            "{\"eventId\":\"shop\",\"trigger\":\"ShopEntered\",\"actions\":[{\"type\":\"record\"}]}]}";
    }
}
