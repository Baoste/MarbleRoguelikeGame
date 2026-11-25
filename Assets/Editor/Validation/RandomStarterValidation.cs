using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static class RandomStarterValidation
    {
        [MenuItem("Marbles ECS/Validate Random Starting Devices")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Run random starter validation in Play Mode.");

            var balance = GameBalanceLoader.Load();
            foreach (var device in balance.Devices) device.StartingCount = 0;
            balance.Devices[0].StartingCount = 1;
            var physics = new EmptyPhysics();
            balance.Campaign.MaxOwnedDevices = balance.Devices[0].PurchaseQuantity;
            ValidateAllStartingDevices(balance, physics);
            balance.Campaign.MaxOwnedDevices = 200;
            using (var simulation = new MarbleSimulation(balance))
            {
                const uint seed = 240127;
                simulation.StartSession(physics, 3, seed);
                uint[] selected = simulation.GetRandomStartingDeviceIds();
                OwnedDeviceSnapshot[] initial = simulation.GetOwnedDevices();
                Check(selected.Length == 3, "three different device packs drawn");
                Check(selected.Distinct().Count() == selected.Length, "no duplicate starter type");
                Check(initial.Length == selected.Sum(id => balance.Devices.Single(d => d.Id == id).PurchaseQuantity),
                    "complete purchase packs created");
                Check(initial.All(d => !d.Placed && d.PurchasePrice == 0), "starter devices await placement and are free");
                foreach (uint id in selected)
                {
                    Check(DeviceResourceCatalog.Current.Get(id).Prefab != null, "selected device has a prefab");
                    var pack = initial.Where(d => d.DefinitionId == id).ToArray();
                    if (pack.Length > 1)
                        Check(pack[0].PairId > 0 && pack.All(d => d.PairId == pack[0].PairId),
                            "paired devices share the normal pair identity");
                }
                Check(simulation.Session.Phase == RoundPhase.Build, "new run waits for placement");
                Check(simulation.Session.Coins == balance.Campaign.StartingCoins, "starter gift does not charge wallet");
                uint expectedState = seed;
                for (int i = 1; i < balance.Devices.Length; i++) MarbleRules.NextRandom(ref expectedState);
                Check(simulation.Context.Round.RandomState == expectedState, "starter shuffle advances the shared run state");

                Check(simulation.BeginRound(), "first round starts");
                Check(simulation.Context.Round.RandomState == expectedState, "first round preserves starter RNG consumption");
                Check(initial.Select(d => d.InstanceId).SequenceEqual(simulation.GetOwnedDevices().Select(d => d.InstanceId)),
                    "starting a round does not redraw devices");
                var round = simulation.Context.Round;
                round.Score = round.TargetScore;
                simulation.Context.Round = round;
                Check(simulation.CashOut(), "first-round cashout works with random starters");
                Check(simulation.Session.Phase == RoundPhase.Shop, "cashout reaches the shop");
                Check(simulation.EnterBuild(), "second-round placement is available");
                Check(initial.Select(d => d.InstanceId).SequenceEqual(simulation.GetOwnedDevices().Select(d => d.InstanceId)),
                    "entering next-round placement does not redraw inventory");
                Check(simulation.BeginRound(), "second round starts");

                simulation.StartSession(physics, 3, seed);
                Check(selected.SequenceEqual(simulation.GetRandomStartingDeviceIds()), "same seed reproduces the draw");
                Check(simulation.GetOwnedDevices().Length == initial.Length, "restart replaces rather than appends inventory");
                simulation.StartSession(physics);
                Check(simulation.GetRandomStartingDeviceIds().Length == 0, "disabled mode has no random draw");
                Check(simulation.Context.Round.RandomState == balance.Marble.RandomSeed, "disabled starters leave the configured seed untouched");
                Check(simulation.GetOwnedDevices().Length == balance.Devices[0].PurchaseQuantity,
                    "disabled mode preserves the authored starter");
            }

            var outcomes = new HashSet<uint>();
            for (uint seed = 1; seed <= 64; seed++)
            {
                uint state = seed;
                foreach (uint id in RandomStartingDevices.Select(balance.Devices, 1, 200, ref state))
                    outcomes.Add(id);
            }
            uint untouched = 123;
            Check(RandomStartingDevices.Select(balance.Devices, 0, 200, ref untouched).Length == 0 && untouched == 123,
                "disabled starter selection consumes no random draws");
            Check(outcomes.Count > 1, "different seeds sample different device types");
            var pair = balance.Devices.FirstOrDefault(d => d.PurchaseQuantity > 1);
            if (pair != null)
            {
                uint state = 1;
                Check(RandomStartingDevices.Select(new[] { pair }, 1, pair.PurchaseQuantity - 1, ref state).Length == 0,
                    "insufficient capacity never creates half a pair");
                var pairedBalance = balance.Copy();
                foreach (var device in pairedBalance.Devices) device.StartingCount = 0;
                pairedBalance.Devices = new[] { pair.Copy() };
                using (var pairedSimulation = new MarbleSimulation(pairedBalance))
                {
                    pairedSimulation.StartSession(physics, 1, 1);
                    var pack = pairedSimulation.GetOwnedDevices();
                    Check(pack.Length == pair.PurchaseQuantity &&
                        pack.All(d => d.PairId == pack[0].PairId && d.PairId > 0),
                        "actual paired starter creates its whole linked group");
                }
            }
            Debug.Log("RANDOM_STARTER_VALIDATION_PASSED: full catalog, random draws, pairs, wallet, restart and two-round flow.");
        }

        private static void ValidateAllStartingDevices(GameBalance balance, IMarblePhysics physics)
        {
            const uint seed = 240127;
            using (var simulation = new MarbleSimulation(balance))
            {
                simulation.StartSession(physics, grantAllStartingDevices: true, randomSeed: seed);
                var inventory = simulation.GetOwnedDevices();
                int count = balance.Devices.Sum(d => d.PurchaseQuantity);
                Check(inventory.Length == count && inventory.All(d => !d.Placed && d.PurchasePrice == 0),
                    "full starter mode grants every device pack to the backpack for free");
                foreach (var definition in balance.Devices)
                {
                    var pack = inventory.Where(d => d.DefinitionId == definition.Id).ToArray();
                    Check(pack.Length == definition.PurchaseQuantity, "one complete starter pack for " + definition.Name);
                    if (pack.Length > 1)
                        Check(pack.All(d => d.PairId > 0 && d.PairId == pack[0].PairId), "starter pack shares its pair identity");
                }
                Check(simulation.Session.Phase == RoundPhase.Build && simulation.Session.Coins == balance.Campaign.StartingCoins,
                    "full starter mode starts in placement at the configured wallet balance");
                Check(simulation.GetRandomStartingDeviceIds().Length == 0 && simulation.Context.Round.RandomState == seed,
                    "granting the full catalog consumes no shared random draws");
                Check(simulation.BeginRound() && simulation.GetOwnedDevices().Length == count,
                    "beginning play carries the starting backpack into the first round");
                simulation.StartSession(physics, grantAllStartingDevices: true, randomSeed: seed);
                Check(simulation.GetOwnedDevices().Length == count, "restarting replaces the full starting inventory");
                simulation.StartSession(physics, grantAllStartingDevices: false, randomSeed: seed);
                Check(simulation.GetOwnedDevices().Length == balance.Devices[0].PurchaseQuantity,
                    "turning off full starter mode uses the authored starting count");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Random starter validation: " + message);
        }

        private sealed class EmptyPhysics : IMarblePhysics
        {
            public bool TrySpawn(MarbleSpawnData data) => false;
            public bool TryGetPosition(MarbleKey key, out Vector3 position)
            { position = Vector3.zero; return false; }
            public void Remove(MarbleKey key) { }
        }
    }
}
