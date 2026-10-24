using System;
using System.Linq;
using MarblesECS.PhysX;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private sealed class ScoreStackFixture : IDisposable
        {
            internal readonly FakePhysics Physics = new FakePhysics();
            internal readonly MarbleSimulation Simulation;
            internal SimulationContext C => Simulation.Context;
            internal ScoreStackFixture(bool content)
            {
                var balance = Balance();
                balance.Content.Definition(GameAttribute.BALL_BASE_VALUE).DefaultValue = 20;
                balance.Marble.BaseScore = 20;
                Simulation = content ? new MarbleSimulation(balance) : new MarbleSimulation(balance.Marble);
                Simulation.StartRound(Physics);
                if (content) Simulation.BeginRound();
            }
            internal void Step(params MarbleContact[] contacts)
            {
                Simulation.BeforePhysics(.01f, Physics, Vector3.up * 2, Quaternion.identity);
                foreach (var hit in contacts) Simulation.EnqueueContact(hit);
                Simulation.AfterPhysics(Physics);
            }
            internal MarbleKey Launch()
            {
                Simulation.FireOnce(); Step();
                return C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
            }
            internal void Advance(double seconds)
            {
                double until = C.Round.Time + seconds;
                while (C.Round.Time < until - 1e-7) Step();
            }
            public void Dispose() => Simulation.Dispose();
        }

        private static MarbleContact StackHit(MarbleKey key, double multiplier = 1, bool enabled = true,
            MarbleContactKind kind = MarbleContactKind.Score, int target = 850) => new MarbleContact
        {
            Key = key, Kind = kind, TargetId = target, Multiplier = multiplier,
            RushDisabled = true, ScoreStackEnabled = enabled
        };

        private static void ScoreStackValidation()
        {
            int start = assertions;
            foreach (bool content in new[] { false, true })
            {
                using (var f = new ScoreStackFixture(content))
                {
                    var first = f.Launch(); var second = f.Launch(); var third = f.Launch();
                    var entity = f.C.Marbles[first];
                    var score = f.C.Manager.GetComponentData<MarbleScore>(entity);
                    score.AddDeviceMultiplier(1.5); score.AddDeviceMultiplier(1.5);
                    f.C.Manager.SetComponentData(entity, score);
                    var hit = StackHit(first);
                    f.Step(hit, hit);
                    Near(42, f.C.Round.Score, "first ball scores value * (zone + .05) * additive device factor");
                    Near(.05, f.Simulation.Snapshot.ScoreStackBonus, "duplicate terminal contacts add only one stack");
                    Near(1, f.Simulation.Snapshot.ScoreStackSecondsRemaining, "first stack starts one second");
                    var scored = f.C.ScoreEvents.Dequeue();
                    Near(1.05, scored.ZoneMultiplier, "score event includes effective zone multiplier");
                    Near(.05, scored.ScoreStackBonus, "score event identifies shared addition");
                    f.Step(StackHit(second, 3, target: 851));
                    Near(104, f.C.Round.Score, "different zone shares the accumulated bonus");
                    Near(.1, f.Simulation.Snapshot.ScoreStackBonus, "second ball adds .05 to the same board counter");
                    Near(1 / 1.05, f.Simulation.Snapshot.ScoreStackSecondsRemaining, "refresh shrinks with the pre-entry bonus");
                    Near(3.1, f.C.ScoreEvents.Dequeue().ZoneMultiplier, "base zone factor is not multiplied by the stack");
                    double deadline = f.C.Round.ScoreStackExpiresAt;
                    f.Step(StackHit(third, 2, false));
                    Near(144, f.C.Round.Score, "disabled zone gets only its original multiplier");
                    Near(.1, f.Simulation.Snapshot.ScoreStackBonus, "disabled zone does not increment");
                    Near(deadline, f.C.Round.ScoreStackExpiresAt, "disabled zone does not refresh");
                    Near(0, f.C.ScoreEvents.Dequeue().ScoreStackBonus, "disabled event reports no stack benefit");
                    var drain = f.Launch(); f.Step(StackHit(drain, kind: MarbleContactKind.Drain));
                    Near(deadline, f.C.Round.ScoreStackExpiresAt, "drain never refreshes even if switch is enabled");
                    Near(.1, f.Simulation.Snapshot.ScoreStackBonus, "drain never increases stack");
                    f.Step(StackHit(first));
                    Near(.1, f.Simulation.Snapshot.ScoreStackBonus, "already settled ball cannot add again");
                    f.Advance(1);
                    Near(0, f.Simulation.Snapshot.ScoreStackBonus, "idle expiry clears the shared addition");
                    Near(0, f.Simulation.Snapshot.ScoreStackSecondsRemaining, "expired timer is cleared");
                    Near(144, f.C.Round.Score, "expiry never removes previously earned score");
                    f.Step(StackHit(f.Launch()));
                    Near(.05, f.Simulation.Snapshot.ScoreStackBonus, "new sequence starts from .05");
                    Near(1, f.Simulation.Snapshot.ScoreStackSecondsRemaining, "new sequence restores initial duration");
                    f.Simulation.StartRound(f.Physics);
                    Near(0, f.Simulation.Snapshot.ScoreStackBonus, "restart clears bonus");
                    Near(0, f.Simulation.Snapshot.ScoreStackSecondsRemaining, "restart clears deadline");
                }
                using (var f = new ScoreStackFixture(content))
                {
                    var first = f.Launch(); var second = f.Launch();
                    f.Step(StackHit(second), StackHit(first));
                    var events = f.C.ScoreEvents.ToArray();
                    Check(events.Length == 2 && events[0].Key.Equals(first), "same-step balls settle in stable spawn order");
                    Near(1.05, events[0].ZoneMultiplier, "earlier ball only gets first stack");
                    Near(1.1, events[1].ZoneMultiplier, "later ball gets second stack");
                    Near(43, f.C.Round.Score, "simultaneous balls are not retroactively repriced");
                    var round = f.C.Round; round.RushRemaining = 5; f.C.Round = round;
                    f.Step(StackHit(f.Launch()));
                    Near(.15, f.Simulation.Snapshot.ScoreStackBonus, "RUSH and score stack run independently");
                    f.C.ScoreEvents.Clear();
                    var invalid = StackHit(f.Launch()); invalid.Multiplier = double.NaN;
                    f.Step(invalid);
                    Near(.15, f.Simulation.Snapshot.ScoreStackBonus, "invalid settlement never increments");
                }
                using (var f = new ScoreStackFixture(content))
                {
                    f.Step(StackHit(f.Launch()));
                    var key = f.Launch();
                    var round = f.C.Round; round.ScoreStackExpiresAt = round.Time + .01; f.C.Round = round;
                    f.Step(StackHit(key));
                    Near(.05, f.Simulation.Snapshot.ScoreStackBonus, "ball at expiry starts a new sequence");
                    Near(1, f.Simulation.Snapshot.ScoreStackSecondsRemaining, "expiry boundary restores one second");
                    for (int i = 0; i < 190; i++) f.Step(StackHit(f.Launch()));
                    Near(.1, f.Simulation.Snapshot.ScoreStackSecondsRemaining, "long sequence respects minimum window");
                    Check(f.Simulation.Snapshot.ScoreStackBonus > 9, "minimum duration does not prevent further increments");
                    round = f.C.Round; round.Phase = (byte)RoundPhase.Draining; f.C.Round = round;
                    RoundFlowSystem.Execute(f.C);
                    Near(0, f.Simulation.Snapshot.ScoreStackBonus, "round completion clears bonus");
                    Near(0, f.Simulation.Snapshot.ScoreStackSecondsRemaining, "round completion clears timer");
                }
            }
            using (var f = new ScoreStackFixture(true))
            {
                f.Simulation.ApplyGlobalAttribute(854, GameAttribute.GAMBLE_POT_VALUE, AttributeOperation.Set, 1);
                var first = f.Launch();
                f.Step(StackHit(first, kind: MarbleContactKind.RandomScore));
                Near(21, f.C.Round.PendingScore, "gambling zone stacks before entering pending score");
                Near(.05, f.Simulation.Snapshot.ScoreStackBonus, "gambling and regular zones share stack");
                var second = f.Launch(); var deadline = f.C.Round.ScoreStackExpiresAt;
                Check(ContentSettlementSystem.TrySettleAmount(f.C, f.C.Marbles[second], StackHit(second), 17), "explicit device settlement succeeds");
                Near(17, f.C.Round.Score, "Lua explicit score is not silently multiplied");
                Near(deadline, f.C.Round.ScoreStackExpiresAt, "device settlement does not refresh zone combo");
                var third = f.Launch(); var flat = StackHit(third); flat.FlatScore = 7;
                f.Step(flat);
                Near(46, f.C.Round.Score, "flat reward stays outside zone multiplier");
            }
            var tuning = new MarbleTuning { ScoreStackIncrement = 0 };
            ExpectArgument(tuning.Validate);
            tuning.ScoreStackIncrement = double.NaN; ExpectArgument(tuning.Validate);
            tuning.ScoreStackIncrement = .05; tuning.ScoreStackInitialSeconds = 0; ExpectArgument(tuning.Validate);
            tuning.ScoreStackInitialSeconds = 1; tuning.ScoreStackMinimumSeconds = 2; ExpectArgument(tuning.Validate);
            ScoreStackPhysicalValidation();
            Debug.Log("SCORE_STACK_VALIDATION_PASSED: " + (assertions - start) + " assertions; shared additive zones, expiry, shrinking windows, opt-out, settlement ordering, resets and real PhysX.");
        }

        private static void ScoreStackPhysicalValidation()
        {
            var scene = SceneManager.CreateScene("ScoreStackPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance());
            var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact);
            var go = new GameObject("StackZone"); SceneManager.MoveGameObjectToScene(go, scene);
            try
            {
                go.transform.position = new Vector3(0, 2, 2);
                var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(3, 3, .2f);
                var zone = go.AddComponent<MarbleZone>(); zone.Kind = MarbleContactKind.Score; zone.TargetId = 858; zone.Multiplier = 2; zone.RushEnabled = false;
                Check(new SerializedObject(zone).FindProperty("ScoreStackEnabled") != null, "Inspector exposes zone participation switch");
                simulation.StartSession(bridge); simulation.BeginRound();
                simulation.ApplyGlobalAttribute(859, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var c = simulation.Context;
                void Step() { simulation.BeforePhysics(.01f, bridge, Vector3.up * 2, Quaternion.identity); bridge.Simulate(.01f); simulation.AfterPhysics(bridge); }
                Physics.SyncTransforms();
                foreach (bool enabled in new[] { true, false, true })
                {
                    zone.ScoreStackEnabled = enabled;
                    simulation.FireOnce(); Step(); var key = c.Marbles.Keys.Single();
                    Check(bridge.TryMove(key, new Vector3(0, 2, 1.5f), Vector3.forward * 5), "move real ball toward zone");
                    for (int i = 0; i < 30 && c.Marbles.ContainsKey(key); i++) Step();
                    Check(!c.Marbles.ContainsKey(key), "real trigger settles and removes ball");
                    var scored = c.ScoreEvents.Dequeue();
                    Near(enabled ? 2 + ScoreStackSystem.Bonus(c) : 2, scored.ZoneMultiplier, "real collider forwards the configured switch");
                }
                Near(.1, simulation.Snapshot.ScoreStackBonus, "two physical enabled entries increment, disabled entry does not");
            }
            finally { simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(go); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
