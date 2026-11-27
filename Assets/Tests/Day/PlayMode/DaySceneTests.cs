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
using UnityEngine.UI;
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
            });
            var game = InTestScene<MarbleGameController>();
            yield return Until(() => game.IsReady, "TestScene controller startup");
            localScene = game.PhysicsRoot.gameObject.scene;
            Assert.That(game.Day.Status, Is.EqualTo(DayCampaignStatus.NotStarted));
            Assert.That(game.Snapshot.Phase, Is.EqualTo(RoundPhase.Playing));
            Assert.That(game.Session.TargetScore, Is.EqualTo(game.Balance.Marble.TargetScore));
            Assert.That(game.Session.BiomassScrapBalance, Is.Zero);
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
        public IEnumerator ConfiguredStartupUsesDailyProductionWithRepeatedLiveInstallation()
        {
            const long initialScrap = 99;
            yield return LoadTestScene(controller => controller.ConfigureDayCampaign((_, campaign) =>
            {
                var day = campaign.Days[0];
                var ids = new uint[] { 9033 };
                var loadout = new DailyLoadoutDefinition(ids, ids, Array.Empty<uint>(), 1, 1,
                    LoadoutSelectionMode.AutoSelectAvailable, day.DailyLoadout.Shop);
                return campaign.WithDays(day.WithSetup(day.BoardLayoutFile, day.BoardLayout, initialScrap, loadout,
                    skipMorningPrep: true));
            }));
            var game = InTestScene<MarbleGameController>();
            yield return Until(() => game.IsReady, "production startup");
            localScene = game.PhysicsRoot.gameObject.scene;
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.Production));
            Assert.That(game.Session.Phase, Is.EqualTo(RoundPhase.Build));
            Assert.That(game.CurrentDayDefinition.BoardLayoutFile, Is.EqualTo("Day0.json"));
            Assert.That(game.Snapshot.NextMarbleBaseValue, Is.EqualTo(.5));
            Assert.That(game.Session.BiomassScrapBalance, Is.EqualTo(initialScrap));
            Assert.That(game.GetOwnedDevices(), Is.Empty);
            var brain = InTestScene<CinemachineBrain>();
            var rig = InTestScene<DiegeticMachineUI>();
            yield return Until(() => brain.ActiveVirtualCamera?.Name == "VCamera_Machine" && rig.IsBoardOpen,
                "machine camera and build controls are active");
            Assert.That(InTestScene<MorningPrepController>().FadeAlpha, Is.Zero);
            rig.RefreshInventory();
            var wallet = rig.ItemGroup.transform.Find("钱包余额").GetComponent<TMP_Text>();
            StringAssert.Contains($"血肉残料 {initialScrap}", wallet.text);
            long price = game.GetDailyPegs().Single().InstallPrice;
            Assert.That(TryDragEconomyPeg(game, rig), Is.True);
            Assert.That(game.Session.BiomassScrapBalance, Is.EqualTo(initialScrap - price));
            Assert.That(rig.TryContinue(), Is.True);
            yield return null;
            Assert.That(TryDragEconomyPeg(game, rig), Is.True, "the same type can be installed while playing");
            Assert.That(game.GetOwnedDevices().Count(device => device.DefinitionId == 9033 && device.Placed), Is.EqualTo(2));
            Assert.That(game.Session.BiomassScrapBalance, Is.EqualTo(initialScrap - 2 * price));
        }

        [UnityTest]
        public IEnumerator ConfiguredDaysOpenTheLoadoutTerminalAndUnlockDrawsOnDayTwo()
        {
            var knownWorlds = OwnedWorlds();
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
            Assert.That(game.Session.TargetScore, Is.EqualTo(game.CurrentDayDefinition.TargetScore));
            var scoreZone = game.PhysicsRoot.GetComponentsInChildren<MarbleZone>().Single();
            var pins = scoreZone.transform.parent.GetComponentsInChildren<MarblePin>();
            var configuredPins = game.CurrentDayDefinition.BoardLayout.Pins.Where(pin => !pin.collisionOnly).ToArray();
            Assert.That(pins, Has.Length.EqualTo(configuredPins.Length));
            var projection = game.PlacementSurface.LayoutProjection;
            for (int i = 0; i < pins.Length; i++)
            {
                var data = configuredPins[i];
                Assert.That(pins[i].gameObject.scene, Is.EqualTo(localScene));
                Assert.That(pins[i].transform.localPosition, Is.EqualTo(projection.ToSurfaceLocal(data.x, data.y, data.radius * 2)));
                Assert.That(pins[i].ScoreMultiplier, Is.EqualTo(data.scoreMultiplier));
                Assert.That(pins[i].RushChanceAdd, Is.EqualTo(data.rushChanceAdd));
            }
            Assert.That(scoreZone.Kind, Is.EqualTo(MarbleContactKind.Score));
            Assert.That(scoreZone.RushEnabled, Is.True);
            Assert.That(scoreZone.GetComponent<BoxCollider>().isTrigger, Is.True);
            Assert.That(game.BeginRound(), Is.False);
            Assert.That(prep.FadeAlpha, Is.GreaterThan(0), "wake-up starts black");
            Assert.That(prep.TryInteract(), Is.False);
            var origin = walker.transform.position;
            walker.Move(Vector2.up, .25f);
            Assert.That(walker.transform.position, Is.EqualTo(origin));

            yield return Until(() => walker.CanMove, "fade finished and room camera is active");
            Assert.That(brain.ActiveVirtualCamera.Name, Is.EqualTo("VCamera_Free"));
            var shopAnchor = prep.transform.Find("Shop Terminal Anchor");
            Assert.That(shopAnchor.childCount, Is.EqualTo(2), "the loadout terminal is present with draws closed");
            var terminal = shopAnchor.Find("Shop Terminal Interaction");
            var shop = InTestScene<ShopUI>();
            var rig = InTestScene<DiegeticMachineUI>();
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
            Assert.That(game.TryCompleteWakeUp(game.Day.Version, out _), Is.False, "the loadout also needs confirmation");
            WalkTo(walker, notePoint.x - 5, -13);
            WalkTo(walker, terminal.position.x - 5, -13);
            WalkTo(walker, terminal.position.x - 5, terminal.position.z);
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => shop.ShopCanvasGroup.interactable, "Day0 loadout opens at the terminal");
            var daily = shop.ShopCanvasGroup.transform.Find("Daily Loadout");
            Assert.That(daily.Find("商店抽取/标题").GetComponent<TMP_Text>().text, Does.Contain("暂时关闭"));
            var cards = daily.Find("可选装置").GetComponentsInChildren<Button>();
            Assert.That(cards, Has.Length.EqualTo(2));
            Assert.That(daily.Find("可选装置").GetComponentsInChildren<RawImage>().All(icon => icon.texture), Is.True);
            foreach (var card in cards) card.onClick.Invoke();
            daily.Find("确认背包").GetComponent<Button>().onClick.Invoke();
            yield return Until(() => walker.CanMove, "confirming Day0 returns to the room");
            WalkTo(walker, terminal.position.x - 5, -13);
            WalkTo(walker, -14, -13);
            WalkTo(walker, -14, -2);
            Assert.That(prep.CanUseMachine, Is.True);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp), "production waits for the priority blend");
            Assert.That(walker.CanMove, Is.False);
            yield return Until(() => game.Day.Phase == DayPhase.Production && rig.IsBoardOpen,
                "machine camera takes over and opens Build");
            Assert.That(rig.TryContinue(), Is.True);
            Assert.That(brain.ActiveVirtualCamera.Name, Is.EqualTo("VCamera_Machine"));
            Assert.That(brain.IsBlending, Is.False);
            Assert.That(walker.CanMove, Is.False);
            Assert.That(game.HasProductionFeature(ProductionFeature.Shop), Is.False);
            Assert.That(game.HasProductionFeature(ProductionFeature.DevicePlacement), Is.True);
            Assert.That(game.HasProductionFeature(ProductionFeature.Rush), Is.True);
            Assert.That(game.HasProductionFeature(ProductionFeature.GamblingWheel), Is.False);
            Assert.That(game.GetShopOffers(), Is.Empty);
            Assert.That(rig.IsBoardOpen, Is.True, "the type panel remains usable during production");
            Assert.That(rig.VisibleDeviceCount, Is.EqualTo(2));
            Assert.That(TryDragEconomyPeg(game, rig), Is.True, "the world-space purchase card installs in Playing");
            Assert.That(game.GetOwnedDevices().Single().DefinitionId, Is.EqualTo(9033));
            long expectedScrap = game.CurrentDayDefinition.InitialBiomassScrap -
                game.Balance.Devices.Single(device => device.Id == 9033).InstallPrice;
            Assert.That(game.Session.BiomassScrapBalance, Is.EqualTo(expectedScrap));
            var library = InTestScene<ItemLibraryUI>();
            library.Open();
            Assert.That(library.IsOpen, Is.False);
            var status = InTestScene<ScoreListener>();
            yield return Until(() => status.RushText.enabled && status.RushSlider.gameObject.activeSelf, "Day0 shows the RUSH meter");
            Assert.That(InTestScene<GamblingWheelDisplay>().DisplayGroup.alpha, Is.Zero);
            var curve = game.CurrentDayDefinition.BloodProgression;
            if (curve?.Stages.Count > 0)
            {
                var bloodView = rig.BloodBar.transform.Find("Blood Value Progression");
                Assert.That(bloodView, Is.Not.Null);
                Assert.That(rig.BloodBar.maxValue, Is.EqualTo(curve.InitialBlood));
                Assert.That(bloodView.GetComponentsInChildren<Image>(), Has.Length.EqualTo(curve.Stages.Count - 1));
                Assert.That(bloodView.Find("Next Marble Value").GetComponent<TMP_Text>().text,
                    Does.EndWith($"{game.Snapshot.NextMarbleBaseValue:G4}"));
            }

            game.AutoMoveLauncher = false;
            game.SetLauncherPosition(game.LauncherCenterZ);
            game.SetFlow(0f);
            // A real score zone hit should activate RUSH and meet the target at any authored pin spacing.
            scoreZone.FlatScore = game.Session.TargetScore;
            var manager = OwnedWorlds().Single(world => !knownWorlds.Contains(world)).EntityManager;
            using (var query = manager.CreateEntityQuery(typeof(RoundData)))
            {
                var entity = query.GetSingletonEntity();
                var round = manager.GetComponentData<RoundData>(entity);
                round.RushMeter = RushSystem.Threshold - RushSystem.InitialContribution;
                manager.SetComponentData(entity, round);
            }
            int zoneHits = 0;
            game.Scored += item => { if (item.ZoneId == scoreZone.TargetId && !item.IsPending) zoneHits++; };
            game.SetFireHeld(true);
            float deadline = Time.realtimeSinceStartup + 20;
            while (game.Snapshot.RushSecondsRemaining == 0 && game.Day.Attempt == 1 && Time.realtimeSinceStartup < deadline)
                yield return null;
            game.SetFireHeld(false);
            Assert.That(zoneHits, Is.GreaterThan(0));
            Assert.That(game.Snapshot.RushSecondsRemaining, Is.Positive,
                $"Day0 RUSH after {zoneHits} Score zone hits, with {game.Snapshot.Blood} blood and meter at {game.Snapshot.RushMeter}");
            Assert.That(game.Session.CanCashOut, Is.True,
                $"Day0 target after {zoneHits} Score zone hits, with {game.Snapshot.Blood} blood and {game.Snapshot.ActiveMarbles} marbles remaining");
            Assert.That(game.GamblingWheel.BallCount, Is.Zero);
            Assert.That(game.CashOut(), Is.True);
            yield return Until(() => game.Day.Phase == DayPhase.DaySettlement, "Day0 settlement");
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.DayId, Is.EqualTo("day1"));
            Assert.That(game.Session.TargetScore, Is.EqualTo(game.CurrentDayDefinition.TargetScore));
            Assert.That(game.HasProductionFeature(ProductionFeature.DevicePlacement | ProductionFeature.Rush), Is.True);
            Assert.That(game.HasProductionFeature(ProductionFeature.Shop), Is.False);
            yield return Until(() => walker.CanMove, "Day1 Morning Prep begins");
            Assert.That(shopAnchor.childCount, Is.EqualTo(2), "Day1 spawns the shop terminal");
            Assert.That(game.Day.MorningPrepRemaining, Is.EqualTo(1));

            WalkTo(walker, notePoint.x - 5, -13);
            WalkTo(walker, notePoint.x - 5, notePoint.z);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(prep.IsReading, Is.True);
            Assert.That(displayedNote.text, Is.EqualTo(game.CurrentDayDefinition.MorningPrep.Single().Text));
            Assert.That(displayedNote.text, Does.Contain($"{game.CurrentDayDefinition.TargetScore} 分"));
            string printedCharacters = string.Concat(displayedNote.text.Where(character => !char.IsWhiteSpace(character)));
            Assert.That(displayedNote.font.HasCharacters(printedCharacters, out uint[] missing, true, true), Is.True,
                "Missing note characters: " + string.Join(", ", missing ?? Array.Empty<uint>()));
            Canvas.ForceUpdateCanvases();
            displayedNote.ForceMeshUpdate();
            Assert.That(displayedNote.isTextOverflowing, Is.False, "the whole announcement fits inside the note card");
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => walker.CanMove, "finished reading the Day1 announcement");

            WalkTo(walker, notePoint.x - 5, -13);
            WalkTo(walker, terminal.position.x - 5, -13);
            WalkTo(walker, terminal.position.x - 5, terminal.position.z);
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => shop.ShopCanvasGroup.interactable, "Day1 shop opens at the terminal");
            Assert.That(game.GetShopOffers(), Is.Empty);
            Assert.That(daily.Find("可选装置").GetComponentsInChildren<Button>(), Has.Length.EqualTo(5));
            Assert.That(game.TryToggleLoadoutPeg(game.Day.Version, 9035), Is.True);
            daily.Find("确认背包").GetComponent<Button>().onClick.Invoke();
            yield return Until(() => walker.CanMove, "Day1 loadout returns to the room");

            WalkTo(walker, terminal.position.x - 5, -13);
            WalkTo(walker, -14, -13);
            WalkTo(walker, -14, -2);
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => game.Day.Phase == DayPhase.Production && rig.IsBoardOpen, "Day1 opens device placement");
            Assert.That(game.Snapshot.Phase, Is.EqualTo(RoundPhase.Build));
            Assert.That(rig.TryContinue(), Is.True);
            game.SetLauncherPosition(game.LauncherCenterZ);
            game.SetFlow(1f);
            game.PhysicsRoot.GetComponentsInChildren<MarbleZone>().Single().FlatScore = game.Session.TargetScore;
            game.SetFireHeld(true);
            yield return Until(() => game.Session.CanCashOut || game.Day.Phase == DayPhase.DaySettlement,
                "Day1 reaches its target through the generated score zone");
            game.SetFireHeld(false);
            if (game.Session.CanCashOut) Assert.That(game.CashOut(), Is.True);
            yield return Until(() => game.Day.Phase == DayPhase.DaySettlement, "Day1 settlement");
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.DayId, Is.EqualTo("day2"));
            yield return Until(() => walker.CanMove, "Day2 Morning Prep begins");
            WalkTo(walker, -14, -13);
            WalkTo(walker, terminal.position.x - 5, -13);
            WalkTo(walker, terminal.position.x - 5, terminal.position.z);
            Assert.That(prep.TryInteract(), Is.True);
            yield return Until(() => shop.ShopCanvasGroup.interactable, "Day2 permission shop opens");
            Assert.That(game.GetShopOffers(), Has.Length.EqualTo(4));
            long scrip = game.Session.CompanyScripBalance;
            var first = game.GetShopOffers()[0];
            daily.Find("商店抽取").GetComponentsInChildren<Button>().First(button => button.interactable).onClick.Invoke();
            Assert.That(game.GetShopOffers()[0].Sold, Is.True);
            Assert.That(game.Session.CompanyScripBalance, Is.EqualTo(scrip - first.Price));
            Assert.That(game.GetOwnedDevices(), Is.Empty);
            Assert.That(daily.Find("反馈").GetComponent<TMP_Text>().text, Does.Contain(first.Name));
        }

        [UnityTest]
        public IEnumerator BloodBarMarksTheThresholdsAndFlashesOnAdvancement()
        {
            testScene = SceneManager.CreateScene("Blood progression HUD");
            board = new GameObject("Blood slider", typeof(RectTransform), typeof(Slider));
            SceneManager.MoveGameObjectToScene(board, testScene);
            var slider = board.GetComponent<Slider>();
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(slider.transform, false);
            slider.fillRect = fill.rectTransform;
            var originalFill = fill.color = Color.red;
            var view = new BloodValueBar(slider);
            var curve = new BloodProgressionDefinition(100, new[]
            {
                new BloodValueStage(0, .5), new BloodValueStage(30, 1), new BloodValueStage(60, 2)
            });
            var snapshot = new MarbleSnapshot
            {
                RoundId = 1, Blood = 100, BloodCapacity = 100, BloodValueStage = 0, NextMarbleBaseValue = .5
            };
            view.Refresh(snapshot, curve);
            var overlay = slider.transform.Find("Blood Value Progression");
            var next = overlay.Find("Next Marble Value").GetComponent<TMP_Text>();
            var ticks = overlay.GetComponentsInChildren<Image>();
            Assert.That(ticks, Has.Length.EqualTo(2));
            Assert.That(ticks[0].rectTransform.anchorMin.x, Is.EqualTo(.7f).Within(1e-6));
            Assert.That(ticks[1].rectTransform.anchorMin.x, Is.EqualTo(.4f).Within(1e-6));
            Assert.That(next.text, Is.EqualTo($"下一颗基础价值 {.5:G4}"));

            snapshot.Blood = 70;
            snapshot.BloodSpent = 30;
            snapshot.BloodValueStage = 1;
            snapshot.NextMarbleBaseValue = 1;
            view.Refresh(snapshot, curve);
            Assert.That(next.text, Is.EqualTo("升值 · 下一颗 1"));
            Assert.That(fill.color, Is.Not.EqualTo(originalFill));
            var reachedColor = ticks[0].color;
            Assert.That(reachedColor, Is.Not.EqualTo(ticks[1].color));
            snapshot.Blood = 75;
            view.Refresh(snapshot, curve);
            Assert.That(ticks[0].rectTransform.anchorMin.x, Is.EqualTo(.75f).Within(1e-6));
            Assert.That(ticks[0].color, Is.EqualTo(reachedColor));

            snapshot.RoundId++;
            snapshot.BloodValueStage = 0;
            snapshot.NextMarbleBaseValue = .5;
            view.Refresh(snapshot, curve);
            Assert.That(next.text, Is.EqualTo($"下一颗基础价值 {.5:G4}"));
            Assert.That(fill.color, Is.EqualTo(originalFill));
            view.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExhaustedPegCanBeReinstalledDuringLivePhysXProduction()
        {
            var balance = GameBalanceLoader.Load();
            balance.Devices.Single(peg => peg.Id == 9033).Durability = 1;
            var game = CreateMachine(balance);
            game.PlacementSurface = board.AddComponent<BoardPlacementSurface>();
            yield return Until(() => game.IsReady, "fixture machine ready");
            localScene = game.PhysicsRoot.gameObject.scene;
            var campaign = GameDaysLoader.Parse(@"{
                ""schemaVersion"":3,""sequenceId"":""live-placement"",""startingCompanyScrip"":3,
                ""days"":[{""dayId"":""first"",""order"":1,""targetScore"":500,""unlockedPegIds"":[9033,9034],
                ""defaultPegIds"":[9033,9034],""maxLoadoutTypeCount"":2,""initialBiomassScrap"":6,""clearCompanyScripReward"":2}]}",
                balance);
            Assert.That(game.TryStartDayCampaign(campaign, out string error, 42), Is.True, error);
            long version = game.Day.Version;
            Assert.That(game.TryOpenShop(version), Is.True);
            Assert.That(game.TryToggleLoadoutPeg(version, 9033), Is.True);
            Assert.That(game.TryToggleLoadoutPeg(version, 9034), Is.True);
            Assert.That(game.TryConfirmLoadout(version), Is.True);
            Assert.That(game.TryCloseShop(version), Is.True);
            Assert.That(game.TryCompleteWakeUp(version, out error), Is.True, error);
            Assert.That(game.BeginRound(), Is.True);
            Assert.That(game.TryInstallDevice(9033, 10, 0, new Vector3(10, 0, 0), 1, 0, Quaternion.identity, out _), Is.False);
            var launch = game.LaunchPoint.TransformPoint(game.LaunchOffset);
            Assert.That(game.TryInstallDevice(9033, launch.x, launch.z, new Vector3(launch.x, 0, launch.z),
                1, 0, Quaternion.identity, out int first), Is.True);
            var firstBody = FindDeviceBody(first);
            Assert.That(firstBody, Is.Not.Null, "projection is live when the install command returns");
            var collider = firstBody.GetComponent<Collider>();
            Assert.That(collider.enabled, Is.True);
            Assert.That(game.TryInstallDevice(9034, launch.x, launch.z, new Vector3(launch.x, 0, launch.z),
                1, 0, Quaternion.identity, out _), Is.False);
            Assert.That(game.TryInstallDevice(9034, 2, 0, new Vector3(2, 0, 0), 1, 0, Quaternion.identity, out int second), Is.True);
            var secondBody = FindDeviceBody(second);
            Assert.That(FindDeviceBody(first), Is.SameAs(firstBody), "another purchase preserves the installed collider");
            Assert.That(game.Session.BiomassScrapBalance, Is.EqualTo(1));
            Assert.That(game.TryInstallDevice(9034, -2, 0, new Vector3(-2, 0, 0), 1, 0, Quaternion.identity, out _), Is.False);

            var broken = new List<DeviceBrokenEvent>();
            game.DeviceBroken += broken.Add;
            game.SetFlow(0);
            game.FireOnce();
            yield return Until(() => broken.Count > 0, "real PhysX collision harvests scrap and breaks the peg");
            Assert.That(broken.Single().InstanceId, Is.EqualTo(first));
            Assert.That(game.Snapshot.ActiveMarbles, Is.EqualTo(1), "the final impact preserves the live ball");
            Assert.That(game.Session.BiomassScrapBalance, Is.EqualTo(2));
            Assert.That(game.Session.CompanyScripBalance, Is.EqualTo(3));
            Assert.That(collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy, Is.True);
            Assert.That(FindDeviceBody(first), Is.Null);
            Assert.That(FindDeviceBody(second), Is.SameAs(secondBody));
            Assert.That(game.GetOwnedDevices().Select(peg => peg.InstanceId), Is.EqualTo(new[] { second }));
            Assert.That(game.DailyLoadout.SelectedCount, Is.EqualTo(2));

            var replacementPose = new Vector3(launch.x, 0, launch.z);
            yield return Until(() => game.CanInstallDevice(9033, launch.x, launch.z, replacementPose, 1, 0, Quaternion.identity),
                "the broken peg's slot is free as soon as the ball clears it");
            Assert.That(game.TryInstallDevice(9033, launch.x, launch.z, replacementPose, 1, 0, Quaternion.identity, out int replacement), Is.True);
            Assert.That(replacement, Is.Not.EqualTo(first));
            Assert.That(FindDeviceBody(replacement).GetComponent<Collider>().enabled, Is.True);
            Assert.That(game.GetOwnedDevices().Single(peg => peg.InstanceId == replacement).RemainingDurability, Is.EqualTo(1));
            Assert.That(game.Session.BiomassScrapBalance, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CollisionOnlyLayoutPinBouncesMarblesWithoutPublishingGameplayContacts()
        {
            testScene = localScene = SceneManager.CreateScene("Collision pin physics", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            board = new GameObject("Fixture Board");
            SceneManager.MoveGameObjectToScene(board, localScene);
            var surface = board.AddComponent<BoardPlacementSurface>();
            var contacts = new List<MarbleContact>();
            using var bridge = new MarblePhysicsBridge(localScene, null, null, contacts.Add);
            bridge.ConfigureBoardLayout(board.transform, surface);
            bridge.ApplyBoardLayout(BoardLayoutLoader.Parse(@"{
                ""schemaVersion"":1,
                ""pins"":[{""x"":0.5,""y"":0.5,""radius"":0.1,""collisionOnly"":true}],
                ""zones"":[]}"));
            var layout = bridge.GeneratedBoardLayout;
            var pin = layout.GetComponentInChildren<CapsuleCollider>();
            Assert.That(pin.enabled && !pin.isTrigger, Is.True);
            Assert.That(pin.GetComponent<MarblePin>(), Is.Null);
            Vector3 center = pin.bounds.center;
            var obstacles = new DevicePlacementObstacles();
            Assert.That(obstacles.IsClear(localScene.GetPhysicsScene(), null, center, .08f, layout), Is.False);

            var key = new MarbleKey(1, 1);
            Assert.That(bridge.TrySpawn(new MarbleSpawnData
            {
                Key = key, Position = center - Vector3.right * .55f, Rotation = Quaternion.identity,
                Quote = new ShotQuote(0, 0, .08f, .1f, 1, 1), LinearVelocity = Vector3.right * 3,
                HasSettings = true,
                Settings = new MarblePhysicalSettings { Radius = .08f, Mass = .1f, Bounce = .6f, MaxSpeed = 12, PierceCount = 1 }
            }), Is.True);

            for (int step = 0; step < 20; step++) bridge.Simulate(.02f);
            Assert.That(bridge.TryGetState(key, out var state), Is.True);
            Assert.That(state.LinearVelocity.x, Is.LessThan(0), "even a marble with a pierce charge hits the collider");
            Assert.That(contacts, Is.Empty);
            yield return null;
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
                var pins = first.GetComponentsInChildren<CapsuleCollider>();
                Assert.That(pins, Has.Length.EqualTo(day0.Pins.Count));
                var obstacles = new DevicePlacementObstacles();
                foreach (var pin in pins)
                    Assert.That(obstacles.IsClear(localScene.GetPhysicsScene(), null, pin.transform.position, .08f, first), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator EnabledMorningShopOpensAtTheTerminalAndReturnsToTheRoom()
        {
            var balance = GameBalanceLoader.Load();
            yield return LoadTestScene(controller =>
            {
                controller.StartWithDayCampaign = false;
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
            var enabled = GameDaysLoader.Parse(@"{""schemaVersion"":3,""sequenceId"":""shop-test"",""startingCompanyScrip"":100,""days"":[
                {""dayId"":""fixture-shop"",""order"":1,""targetScore"":50,""initialBiomassScrap"":8,""clearCompanyScripReward"":0,
                ""unlockedPegIds"":[9033,9034,9035],""defaultPegIds"":[9033],""requiredPegIds"":[9033],""maxLoadoutTypeCount"":2,
                ""shopEnabled"":true,""shopDrawCost"":2,""shopOfferCount"":2,
                ""morningPrep"":[{""objectiveId"":""read-note"",""type"":""ReadNote"",""anchorId"":""desk"",""text"":""fixture note""}]}]}", balance);
            var closed = GameDaysLoader.Parse(@"{""schemaVersion"":2,""sequenceId"":""closed-test"",""days"":[
                {""dayId"":""fixture-closed"",""order"":2,""targetScore"":50}]}", balance);
            var definition = enabled.WithDays(enabled.Days[0], closed.Days[0]);
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
            var firstCard = shop.ShopCanvasGroup.GetComponentsInChildren<Button>()
                .Single(button => button.GetComponentsInChildren<TMP_Text>().Any(label => label.text == stock[0].Name));
            Assert.That(firstCard.IsInteractable(), Is.True);
            long scrip = game.Session.CompanyScripBalance;
            long scrap = game.Session.BiomassScrapBalance;
            firstCard.onClick.Invoke();
            Assert.That(game.GetShopOffers()[0].Sold, Is.True);
            Assert.That(game.GetDailyPegs().Single(peg => peg.DefinitionId == stock[0].DefinitionId).Permitted, Is.True);
            Assert.That(game.GetOwnedDevices(), Is.Empty);
            Assert.That(game.Session.CompanyScripBalance, Is.EqualTo(scrip - stock[0].Price));
            Assert.That(game.Session.BiomassScrapBalance, Is.EqualTo(scrap));
            uint afterPurchase = game.Day.RandomState;

            shop.ShopCanvasGroup.transform.Find("Daily Loadout/返回房间").GetComponent<Button>().onClick.Invoke();
            yield return Until(() => walker.CanMove && !shop.ShopCanvasGroup.blocksRaycasts, "Return to room closes the shop");
            Assert.That(walker.transform.position, Is.EqualTo(roomPosition));
            Assert.That(game.Day.Phase, Is.EqualTo(DayPhase.WakeUp));
            Assert.That(game.BuyOffer(1), Is.False);
            Assert.That(prep.TryInteract(), Is.True);
            Assert.That(game.Day.RandomState, Is.EqualTo(afterPurchase));
            Assert.That(game.GetShopOffers()[0].Sold, Is.True);
            Assert.That(prep.TryInteract(), Is.True, "E also closes the shop");
            Assert.That(prep.TryInteract(), Is.True);
            shop.ShopCanvasGroup.transform.Find("Daily Loadout/确认背包").GetComponent<Button>().onClick.Invoke();
            Assert.That(game.LoadoutConfirmed, Is.True);

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
            yield return Until(() => game.Day.Phase == DayPhase.Production && game.Session.Phase == RoundPhase.Build,
                "machine blend completes");
            Assert.That(brain.ActiveVirtualCamera.Name, Is.EqualTo("VCamera_Machine"));
            Assert.That(game.TryOpenShop(game.Day.Version), Is.False);
            Assert.That(shop.ShopCanvasGroup.alpha, Is.Zero);
            Assert.That(game.BeginRound(), Is.True);
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
            long savedScrap = game.Session.BiomassScrapBalance;
            long savedScrip = game.Session.CompanyScripBalance;
            Assert.That(savedDevice.InstanceId, Is.EqualTo(placedDevice.InstanceId));
            Assert.That(savedDevice.PurchasePrice, Is.EqualTo(placedDevice.PurchasePrice));
            Assert.That(savedDevice.Placed, Is.False, "the next morning starts with the same device in the backpack");
            Assert.That(FindDeviceBody(starterId), Is.Null);

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
            Assert.That(game.Session.BiomassScrapBalance, Is.EqualTo(savedScrap));
            Assert.That(game.Session.CompanyScripBalance, Is.EqualTo(savedScrip));
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
            Assert.That(game.TryOpenShop(game.Day.Version), Is.False, "an inventory-only day has no permission terminal");
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

        private static bool TryDragEconomyPeg(MarbleGameController game, DiegeticMachineUI rig)
        {
            int before = game.GetOwnedDevices().Length;
            var placement = rig.Placement;
            var reference = placement.PlacementPlane;
            var plane = new Plane(reference.up, reference.position);
            foreach (float x in new[] { -2f, -1f, 1f })
                foreach (float z in new[] { -2.25f, 2.25f })
                {
                    var point = plane.ClosestPointOnPlane(placement.BoardRoot.TransformPoint(new Vector3(x, 0, z)));
                    var pointer = rig.ViewCamera.WorldToScreenPoint(point);
                    if (pointer.z <= 0 || !rig.ViewCamera.pixelRect.Contains(pointer) ||
                        !game.CanInstallDevice(9033, x, z, point + reference.up * placement.DevicePlaneDistance,
                            1, 0, reference.rotation) || !rig.BeginDeviceDrag(0, pointer)) continue;
                    rig.EndDeviceDrag(pointer);
                    if (game.GetOwnedDevices().Length > before) return true;
                }
            return false;
        }

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
            "\"days\":[{\"dayId\":\"fixture-first\",\"order\":1,\"targetScore\":4,\"clearCompanyScripReward\":35,\"productionFeatures\":[\"DevicePlacement\",\"Rush\",\"GamblingWheel\"]}," +
            "{\"dayId\":\"fixture-second\",\"order\":2,\"targetScore\":24,\"clearCompanyScripReward\":45,\"initialBiomassScrap\":8,\"eventIds\":[\"round\",\"retry\"]," +
            "\"productionFeatures\":[\"DevicePlacement\",\"Rush\",\"GamblingWheel\"]}]," +
            "\"events\":[{\"eventId\":\"round\",\"trigger\":\"RoundSettled\",\"executionPolicy\":\"EveryOccurrence\",\"actions\":[{\"type\":\"record\"}]}," +
            "{\"eventId\":\"retry\",\"trigger\":\"DayRetried\",\"actions\":[{\"type\":\"record\"}]}]}";
    }
}
