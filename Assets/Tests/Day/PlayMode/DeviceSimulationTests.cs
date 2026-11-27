using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MarblesECS.Tests
{
    public sealed class DeviceSimulationTests
    {
        private const float StepSeconds = .02f;
        private readonly List<Scene> scenes = new List<Scene>();

        [UnityTearDown]
        public IEnumerator UnloadScenes()
        {
            foreach (var scene in scenes)
                if (scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            scenes.Clear();
        }

        [UnityTest]
        public IEnumerator InventoryPlacementCreatesPhysXBodyWithoutAPlacementView()
        {
            var scene = CreateScene();
            var board = CreateNode(scene, "Board", Vector3.zero);
            var obstacle = CreateNode(scene, "Placement obstacle", new Vector3(2, 0, 0));
            obstacle.AddComponent<BoxCollider>();
            var scoreNode = CreateNode(scene, "Score", new Vector3(-3, 1, 0));
            scoreNode.AddComponent<BoxCollider>().isTrigger = true;
            var zone = scoreNode.AddComponent<MarbleZone>();
            zone.TargetId = 42;
            zone.FlatScore = 11;
            zone.Multiplier = 0;
            zone.RushEnabled = zone.ScoreStackEnabled = false;

            var balance = Balance();
            balance.Devices.Single(item => item.Id == 9033).RotationLocked = false;
            using var simulation = new MarbleSimulation(balance);
            using var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact, createVisuals: false);
            bridge.ConfigureDevicePlacement(board.transform, obstacles: new[] { obstacle.transform });
            simulation.StartSession(bridge);
            int id = AddInventory(simulation, 9033);
            Assert.That(simulation.PlaceDevice(id, 2, 0), Is.False);
            Assert.That(simulation.GetOwnedDevices().Single().Placed, Is.False);
            Assert.That(simulation.PlaceDevice(id, 0, 0), Is.True);
            Assert.That(bridge.TryGetDeviceBody(id, out var original), Is.True);
            Assert.That(simulation.PlaceDevice(id, -1, 0), Is.True);
            Assert.That(simulation.RotateDevice(id, 45), Is.True);
            Assert.That(bridge.TryGetDevicePose(id, out var pose), Is.True);
            Assert.That(pose.Position, Is.EqualTo(Vector3.left));
            Assert.That(Vector3.Angle(pose.Forward, Quaternion.Euler(0, 45, 0) * Vector3.back), Is.LessThan(.01));
            int revision = simulation.Session.LayoutRevision;
            Assert.That(simulation.PlaceDevice(id, 2, 0, angle: 90), Is.False);
            Assert.That(simulation.Session.LayoutRevision, Is.EqualTo(revision));
            Assert.That(simulation.GetOwnedDevices().Single().Angle, Is.EqualTo(45));
            Assert.That(bridge.TryGetDevicePose(id, out var unchanged), Is.True);
            Assert.That(unchanged.Position, Is.EqualTo(pose.Position));
            Assert.That(unchanged.Forward, Is.EqualTo(pose.Forward));

            Assert.That(simulation.RemoveDevicePlacement(id), Is.True);
            Assert.That(original.gameObject.activeInHierarchy, Is.False);
            Assert.That(bridge.TryGetDevicePose(id, out _), Is.False);
            Assert.That(simulation.PlaceDevice(id, -1, 0), Is.True);
            Assert.That(bridge.TryGetDeviceBody(id, out var body), Is.True);
            Assert.That(body.Script, Is.SameAs(original.Script), "withdrawal preserves inventory Lua");
            int durability = simulation.GetOwnedDevices().Single().RemainingDurability;
            Assert.That(simulation.BeginRound(), Is.True);
            simulation.ApplyGlobalAttribute(1, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
            var collider = body.GetComponentInChildren<Collider>();
            var launch = collider.bounds.center + Vector3.up;
            var key = Fire(simulation, bridge, launch);
            Assert.That(bridge.TrySetVelocity(key, Vector3.down * 4), Is.True);
            for (int i = 0; i < 50 && simulation.Session.BiomassScrapBalance == 0; i++)
                Step(simulation, bridge, launch);

            Assert.That(simulation.Session.BiomassScrapBalance, Is.EqualTo(1), "a real impact invokes the scrap peg's Lua");
            Assert.That(simulation.GetOwnedDevices().Single().RemainingDurability, Is.EqualTo(durability - 1));
            Assert.That(bridge.TryMove(key, scoreNode.transform.position, Vector3.zero), Is.True);
            Step(simulation, bridge, launch);
            Assert.That(simulation.TryDequeueScore(out var score), Is.True);
            Assert.That(score.ZoneId, Is.EqualTo(zone.TargetId));
            Assert.That(score.Score, Is.EqualTo(11));
            Assert.That(scene.GetRootGameObjects().SelectMany(node => node.GetComponentsInChildren<Renderer>(true)), Is.Empty);
            simulation.Dispose();
            Assert.That(bridge.PlacedDeviceBodies, Is.Empty);
            yield return null;
            Assert.That(original == null && body == null, Is.True);
        }

        [UnityTest]
        public IEnumerator SeparateSessionsKeepIdenticalDeviceIdsAndLuaStateIsolated()
        {
            var sceneA = CreateScene();
            var sceneB = CreateScene();
            using var a = new MarbleSimulation(Balance());
            using var b = new MarbleSimulation(Balance());
            using var bridgeA = new MarblePhysicsBridge(sceneA, null, null, a.EnqueueContact, createVisuals: false);
            using var bridgeB = new MarblePhysicsBridge(sceneB, null, null, b.EnqueueContact, createVisuals: false);
            a.StartSession(bridgeA);
            b.StartSession(bridgeB);
            int id = AddInventory(a, 9001);
            Assert.That(AddInventory(b, 9001), Is.EqualTo(id));
            Assert.That(a.PlaceDevice(id, 0, 0), Is.True);
            Assert.That(b.PlaceDevice(id, 4, 0), Is.True);
            Assert.That(bridgeA.TryGetDeviceBody(id, out var bodyA), Is.True);
            Assert.That(bridgeB.TryGetDeviceBody(id, out var bodyB), Is.True);
            Assert.That(bodyB.Script, Is.Not.SameAs(bodyA.Script));
            var ray = new Ray(Vector3.up * 2, Vector3.down);
            Assert.That(bridgeA.TryPickDevice(ray, out _, clickableOnly: false), Is.True);
            Assert.That(bridgeB.TryPickDevice(ray, out _, clickableOnly: false), Is.False);

            long refund = a.GetOwnedDevices().Single().SellPrice;
            Assert.That(a.SellDevice(id), Is.True);
            Assert.That(a.SellDevice(id), Is.False);
            Assert.That(a.Session.BiomassScrapBalance, Is.EqualTo(refund));
            Assert.That(bridgeA.TryGetDevicePose(id, out _), Is.False);
            Assert.That(bodyA.Script.IsDisposed, Is.True);
            Assert.That(bodyB.Script.IsDisposed, Is.False);
            Assert.That(bridgeB.TryGetDevicePose(id, out var poseB), Is.True);
            Assert.That(poseB.Position.x, Is.EqualTo(4));
            yield return null;
            Assert.That(sceneA.GetRootGameObjects().SelectMany(node => node.GetComponentsInChildren<Collider>()), Is.Empty);
        }

        [UnityTest]
        public IEnumerator FailedPaidInstallationLeavesWalletAndPairUnchanged()
        {
            var balance = Balance();
            var pair = balance.Devices.Single(item => item.Id == 103);
            var source = GameDaysLoader.Load(balance);
            var ids = new[] { pair.Id };
            var loadout = new DailyLoadoutDefinition(ids, ids, Array.Empty<uint>(), 1, 1, LoadoutSelectionMode.AutoSelectAvailable, null);
            var day = source.Days[0].WithSetup(null, null, pair.InstallPrice, loadout, skipMorningPrep: true);
            var scene = CreateScene();
            using var simulation = new MarbleSimulation(balance);
            using var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact, createVisuals: false);
            Assert.That(simulation.TryStartDayCampaign(source.WithDays(day), bridge, out var error), Is.True, error);
            var context = simulation.Context;
            int revision = simulation.Session.LayoutRevision;
            int activeScripts = DeviceLuaRuntime.ActiveBehaviourCount;
            var create = context.DeviceScripts.CreateScript;
            context.DeviceScripts.CreateScript = _ => throw new InvalidOperationException("fixture rejects physical initialization");
            LogAssert.Expect(LogType.Exception, new Regex("fixture rejects physical initialization"));
            Assert.That(simulation.TryInstallDevice(pair.Id, -2, 0, .05f, 0, out _), Is.False);
            Assert.That(simulation.GetOwnedDevices(), Is.Empty);
            Assert.That(bridge.PlacedDeviceBodies, Is.Empty);
            Assert.That(simulation.Session.LayoutRevision, Is.EqualTo(revision));
            Assert.That(simulation.Session.BiomassScrapBalance, Is.EqualTo(pair.InstallPrice));
            Assert.That(DeviceLuaRuntime.ActiveBehaviourCount, Is.EqualTo(activeScripts));
            yield return null;
            Assert.That(scene.GetRootGameObjects().SelectMany(node => node.GetComponentsInChildren<Collider>(true)), Is.Empty);

            context.DeviceScripts.CreateScript = create;
            Assert.That(simulation.TryInstallDevice(pair.Id, -2, 0, .05f, 0, out int first), Is.True);
            Assert.That(simulation.TryInstallDevice(pair.Id, 2, 0, .05f, 0, out int second), Is.True);
            Assert.That(bridge.PlacedDeviceBodies.Select(body => body.InstanceId), Is.EqualTo(new[] { first, second }));
            Assert.That(simulation.GetOwnedDevices().Select(item => item.PairId).Distinct(), Is.EqualTo(new[] { first }));
            Assert.That(simulation.Session.BiomassScrapBalance, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CheckpointReplacesPhysicsAndScriptsOnlyAfterPreparationSucceeds()
        {
            using var simulation = new MarbleSimulation(Balance());
            using var bridge = new MarblePhysicsBridge(CreateScene(), null, null, simulation.EnqueueContact, createVisuals: false);
            simulation.StartSession(bridge);
            var context = simulation.Context;
            int id = AddInventory(simulation, 9001);
            Assert.That(simulation.PlaceDevice(id, -2, 1, angle: 45), Is.True);
            var checkpoint = new DayCheckpoint(context, Array.Empty<DayRuleData>(), preserveMorningProgress: false);
            Assert.That(simulation.PlaceDevice(id, 1, 1, angle: 90), Is.True);
            Assert.That(bridge.TryGetDeviceBody(id, out var original), Is.True);
            CampaignWalletSystem.Set(context, GameCurrency.BiomassScrap, 17);

            var create = context.DeviceScripts.CreateScript;
            context.DeviceScripts.CreateScript = _ => throw new InvalidOperationException("fixture rejects checkpoint initialization");
            LogAssert.Expect(LogType.Exception, new Regex("fixture rejects checkpoint initialization"));
            Assert.Throws<InvalidOperationException>(() => checkpoint.Restore(context, 42));
            Assert.That(simulation.Session.BiomassScrapBalance, Is.EqualTo(17));
            Assert.That(simulation.GetOwnedDevices().Single().X, Is.EqualTo(1));
            Assert.That(original.transform.position.x, Is.EqualTo(1));
            Assert.That(original.Script.IsDisposed, Is.False);

            context.DeviceScripts.CreateScript = create;
            checkpoint.Restore(context, 42);
            Assert.That(bridge.TryGetDeviceBody(id, out var restored), Is.True);
            Assert.That(restored, Is.Not.SameAs(original));
            Assert.That(original.gameObject.activeInHierarchy, Is.False);
            Assert.That(original.Script.IsDisposed, Is.True);
            Assert.That(simulation.Session.BiomassScrapBalance, Is.Zero);
            Assert.That(simulation.GetOwnedDevices().Single().Angle, Is.EqualTo(45));
            Assert.That(restored.transform.position.x, Is.EqualTo(-2));
            Assert.That(bridge.PlacedDeviceBodies, Has.Count.EqualTo(1));
            yield return null;
            Assert.That(original == null, Is.True);
        }

        [UnityTest]
        public IEnumerator BombFuseAndSplitAreIdenticalWithMissingOrDisabledVisuals()
        {
            var headless = RunBomb(withPresentation: false, missingTemplate: false, removeDuringFlight: false);
            foreach (var variant in new[] { RunBomb(true, false, false), RunBomb(true, true, false), RunBomb(true, false, true) })
            {
                Assert.That((variant.random, variant.time, variant.durability),
                    Is.EqualTo((headless.random, headless.time, headless.durability)));
                for (int i = 0; i < headless.positions.Length; i++)
                    Assert.That(Vector3.Distance(variant.positions[i], headless.positions[i]), Is.LessThan(.0001f));
            }
            yield return null;
        }

        private (uint random, double time, int durability, Vector3[] positions) RunBomb(
            bool withPresentation, bool missingTemplate, bool removeDuringFlight)
        {
            var balance = Balance();
            var definition = balance.Devices.Single(item => item.Id == 9017);
            definition.Durability = 1;
            definition.Value = .2;
            var scene = CreateScene();
            using var simulation = new MarbleSimulation(balance);
            using var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact, createVisuals: false);
            using var presentation = withPresentation ? new DevicePresentation(bridge) : null;
            simulation.StartSession(bridge, 42);
            var context = simulation.Context;
            if (missingTemplate)
                context.DeviceScripts.CreateScript = resource => resource.CreateScript(resource.LuaSource.Replace("BombTemplate", "MissingBomb"));
            int id = AddInventory(simulation, definition.Id);
            Assert.That(simulation.PlaceDevice(id, 0, 0), Is.True);
            Assert.That(simulation.BeginRound(), Is.True);
            simulation.ApplyGlobalAttribute(1, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
            var launch = new Vector3(0, 2, 3);
            var key = Fire(simulation, bridge, launch);
            for (int i = 0; i < 30 && !context.Transients.Items.Any(); i++)
                Step(simulation, bridge, launch);
            var flight = context.Transients.Items.Single();
            presentation?.Sync(context);
            Assert.That(bridge.TryGetDeviceBody(id, out var body), Is.True);
            Assert.That(body.transform.Find("TransientVisual " + flight.Id) != null, Is.EqualTo(withPresentation && !missingTemplate));
            if (removeDuringFlight) presentation.Dispose();
            Assert.That(bridge.TryMove(key, flight.PositionAt(flight.StartedAt + flight.Duration), Vector3.zero), Is.True);
            for (int i = 0; i < 65 && context.Marbles.ContainsKey(key); i++)
            {
                Step(simulation, bridge, launch);
                if (!removeDuringFlight) presentation?.Sync(context);
            }
            Assert.That(context.Marbles.ContainsKey(key), Is.False);
            Assert.That(bridge.ActiveBodyCount, Is.EqualTo(8));
            int durability = simulation.GetOwnedDevices().Single().RemainingDurability;
            Assert.That(durability, Is.Zero);
            var positions = context.Marbles.Keys.OrderBy(k => k.Sequence).Select(k =>
            {
                Assert.That(bridge.TryGetPosition(k, out var position), Is.True);
                return position;
            }).ToArray();
            return (context.Round.RandomState, context.Round.Time, durability, positions);
        }

        private Scene CreateScene()
        {
            var scene = SceneManager.CreateScene("Device PhysX " + scenes.Count, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            scenes.Add(scene);
            return scene;
        }

        private static GameObject CreateNode(Scene scene, string name, Vector3 position)
        {
            var node = new GameObject(name);
            SceneManager.MoveGameObjectToScene(node, scene);
            node.transform.position = position;
            return node;
        }

        private static GameBalance Balance()
        {
            var balance = GameBalanceLoader.Load();
            foreach (var device in balance.Devices) device.StartingCount = 0;
            return balance;
        }

        private static int AddInventory(MarbleSimulation simulation, uint definitionId)
        {
            var context = simulation.Context;
            var definition = CampaignStateUtility.Device(context, definitionId);
            CampaignStateUtility.CreateDevice(context, definition, definition.InstallPrice);
            return context.Campaign.NextDeviceId;
        }

        private static MarbleKey Fire(MarbleSimulation simulation, MarblePhysicsBridge bridge, Vector3 launch)
        {
            simulation.FireOnce();
            Step(simulation, bridge, launch);
            return simulation.Context.Marbles.Keys.Single();
        }

        private static void Step(MarbleSimulation simulation, MarblePhysicsBridge bridge, Vector3 launch)
        {
            simulation.BeforePhysics(StepSeconds, bridge, launch, Quaternion.identity);
            bridge.Simulate(StepSeconds);
            simulation.AfterPhysics(bridge);
        }
    }
}
