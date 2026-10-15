using System.Linq;
using MarblesECS;
using NUnit.Framework;
using UnityEngine;

namespace GameAI.Tests
{
    public sealed class GameAIPlacementTests
    {
        private sealed class NoBallsPhysics : IMarblePhysics
        {
            public bool TrySpawn(MarbleSpawnData data) => false;
            public bool TryGetPosition(MarbleKey key, out Vector3 position) { position = default; return false; }
            public void Remove(MarbleKey key) { }
        }

        [Test]
        public void LegalPlacementQueriesDoNotMutateTheBoardAndAgreeWithExecution()
        {
            var balance = GameBalanceLoader.Load();
            foreach (var definition in balance.Devices) definition.StartingCount = 0;
            balance.Devices[0].StartingCount = 2;
            using (var simulation = new MarbleSimulation(balance))
            {
                simulation.StartSession(new NoBallsPhysics());
                var devices = simulation.GetOwnedDevices();
                Assert.That(devices.Length, Is.GreaterThanOrEqualTo(2));
                int revision = simulation.Session.LayoutRevision;
                Assert.That(simulation.CanPlaceDevice(devices[0].InstanceId, 0, 0), Is.True);
                Assert.That(simulation.Session.LayoutRevision, Is.EqualTo(revision));
                Assert.That(simulation.GetOwnedDevices().Any(x => x.Placed), Is.False);
                Assert.That(simulation.PlaceDevice(devices[0].InstanceId, 0, 0), Is.True);
                revision = simulation.Session.LayoutRevision;
                Assert.That(simulation.CanPlaceDevice(devices[1].InstanceId, 0, 0), Is.False);
                Assert.That(simulation.PlaceDevice(devices[1].InstanceId, 0, 0), Is.False);
                Assert.That(simulation.Session.LayoutRevision, Is.EqualTo(revision));
                Assert.That(simulation.CanPlaceDevice(devices[1].InstanceId, float.NaN, 0), Is.False);
            }
        }
    }
}
