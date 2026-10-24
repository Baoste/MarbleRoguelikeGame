using System;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private sealed class FireFixture : IDisposable
        {
            internal readonly FakePhysics Physics = new FakePhysics();
            internal readonly MarbleSimulation Simulation;
            internal SimulationContext C => Simulation.Context;
            internal int Shots => checked((int)C.NextSpawnSequence);

            internal FireFixture(bool content)
            {
                var balance = Balance();
                balance.Marble.FireIntervalSeconds = .15f;
                balance.Marble.HoldAccelerationSeconds = 2;
                Simulation = content ? new MarbleSimulation(balance) : new MarbleSimulation(balance.Marble);
                Simulation.StartRound(Physics);
                if (content) Simulation.BeginRound();
            }

            internal void Step(float dt = .01f)
            {
                Simulation.BeforePhysics(dt, Physics, Vector3.up * 2, Quaternion.identity);
                Simulation.AfterPhysics(Physics);
            }

            internal void Advance(double seconds)
            {
                double end = C.Round.Time + seconds;
                while (C.Round.Time < end - 1e-6) Step();
            }

            public void Dispose() => Simulation.Dispose();
        }

        private static void LauncherCadenceValidation()
        {
            int start = assertions;
            foreach (bool content in new[] { false, true })
            {
                using (var f = new FireFixture(content))
                {
                    for (int i = 0; i < 100; i++) f.Simulation.FireOnce();
                    f.Step();
                    Near(1, f.Physics.Spawns.Count, "many requests in one step produce only one shot");
                    f.Simulation.FireOnce(); f.Step();
                    Near(0, f.C.Launcher.FireRequested, "cooldown rejects the current request");
                    f.Advance(.5);
                    Near(1, f.Physics.Spawns.Count, "over-speed input never fires later");

                    for (int i = 0; i < 4; i++)
                    {
                        f.Simulation.SetFireHeld(true); f.Step(); f.Simulation.SetFireHeld(false);
                        f.Advance(.29);
                    }
                    Near(5, f.Physics.Spawns.Count, "four distinct taps produce four immediate shots");
                    int before = f.Shots;
                    for (int i = 0; i < 100; i++)
                    {
                        f.Simulation.SetFireHeld(true); f.Step(); f.Simulation.SetFireHeld(false);
                    }
                    Check(f.Shots - before >= 6 && f.Shots - before <= 7, "rapid taps respect the maximum cadence");
                    before = f.Shots; f.Advance(1);
                    Near(before, f.Shots, "stopping taps immediately ends shooting without backlog");
                    f.Simulation.SetFireHeld(true); f.Simulation.SetFireHeld(false); f.Step();
                    Near(before, f.Shots, "release cancels an unconsumed press before the next physics step");
                }

                using (var f = new FireFixture(content))
                {
                    f.Simulation.SetFireHeld(true);
                    Near(0, f.C.Launcher.HeldSeconds, "hold ramp starts at zero");
                    f.Advance(1);
                    int firstSecond = f.Shots;
                    Near(1, f.C.Launcher.HeldSeconds, "hold ramp advances with simulation time");
                    f.Simulation.SetFireHeld(true);
                    Near(1, f.C.Launcher.HeldSeconds, "OS key repeat does not restart acceleration");
                    f.Advance(1);
                    int secondSecond = f.Shots - firstSecond;
                    Near(2, f.C.Launcher.HeldSeconds, "hold reaches full rate in two seconds");
                    Check(firstSecond > 0 && secondSecond > firstSecond, "second held second emits more than the first");
                    int before = f.Shots; f.Advance(1);
                    Check(f.Shots - before >= 6 && f.Shots - before <= 7, "warmed hold reaches ordinary maximum cadence");
                    f.Simulation.SetFireHeld(false);
                    Near(0, f.C.Launcher.HeldSeconds, "release resets acceleration");
                    Near(0, f.C.Launcher.HeldShotProgress, "release removes partial repeat progress");
                    before = f.Shots; f.Advance(1);
                    Near(before, f.Shots, "release prevents all later automatic shots");
                    f.Simulation.SetFireHeld(true); f.Step(); f.Advance(.1);
                    Near(before + 1, f.Shots, "a new hold starts slow instead of resuming maximum rate");
                    f.Simulation.ClearFireInput(); before = f.Shots; f.Advance(1);
                    Near(before, f.Shots, "clearing input for pause or UI leaves no delayed shot");
                    Near(0, f.C.Launcher.HeldSeconds, "clearing input also resets the hold ramp");
                }

                using (var f = new FireFixture(content))
                {
                    f.Simulation.SetFireHeld(true); f.Advance(2);
                    var round = f.C.Round; round.RushRemaining = 2; f.C.Round = round;
                    Near(1 / (double)f.C.Tuning.FireIntervalSeconds + 5, f.Simulation.Snapshot.ShotsPerSecond,
                        "RUSH raises the same maximum by five shots per second");
                    int before = f.Shots; float blood = f.C.Player.Blood; f.Advance(1);
                    Check(f.Shots - before >= 10 && f.Shots - before <= 12, "warmed hold accelerates under RUSH");
                    Near(blood, f.C.Player.Blood, "RUSH shooting remains blood-free");
                    f.Advance(1.1);
                    before = f.Shots; f.Advance(1);
                    Check(f.Shots - before >= 6 && f.Shots - before <= 7, "RUSH expiry restores the normal maximum");
                    f.Simulation.SetFireHeld(false); before = f.Shots; f.Advance(.5);
                    Near(before, f.Shots, "RUSH expiry cannot restart firing after release");
                }

                using (var f = new FireFixture(content))
                {
                    f.Physics.FailSpawnAt = 1;
                    f.Simulation.FireOnce(); f.Step();
                    Near(0, f.Physics.Spawns.Count, "failed spawn publishes no ball");
                    Near(1000, f.C.Player.Blood, "failed spawn costs no blood");
                    Near(0, f.C.Launcher.NextFireAt, "failed spawn does not consume cooldown");
                    f.Physics.FailSpawnAt = 0; f.Advance(.5);
                    Near(0, f.Physics.Spawns.Count, "failed one-shot request is not retried without new input");
                    f.Simulation.FireOnce(); f.Step();
                    Near(1, f.Physics.Spawns.Count, "a fresh press can retry after a failed spawn");
                    f.C.Tuning.MaxActiveMarbles = 1;
                    f.Advance(.2); f.Simulation.FireOnce(); f.Step();
                    f.C.Tuning.MaxActiveMarbles = 256; f.Advance(.5);
                    Near(1, f.Physics.Spawns.Count, "capacity recovery never replays rejected input");
                }

                using (var f = new FireFixture(content))
                {
                    var player = f.C.Player; player.Blood = 0; f.C.Player = player;
                    f.Simulation.FireOnce(); f.Step();
                    Near(0, f.Physics.Spawns.Count, "unaffordable press produces no ball");
                    player.Blood = 1000; f.C.Player = player;
                    var round = f.C.Round; round.Phase = (byte)RoundPhase.Playing; f.C.Round = round;
                    f.Advance(.5);
                    Near(0, f.Physics.Spawns.Count, "restored blood never replays rejected input");
                    f.Simulation.SetFireHeld(true); f.Advance(.5);
                    f.Simulation.StartRound(f.Physics);
                    if (content) f.Simulation.BeginRound();
                    Near(0, f.C.Launcher.HeldSeconds, "new round clears hold acceleration");
                    f.Advance(1);
                    Near(0, f.Physics.Spawns.Count, "new round starts without stale firing input");
                }

                using (var f = new FireFixture(content))
                {
                    f.Simulation.SetFireHeld(true);
                    for (int i = 0; i < 4; i++) f.Step(.5f);
                    int before = f.Shots; f.Step(.5f);
                    Near(before + 1, f.Shots, "a long simulation step emits at most one shot");
                    f.Simulation.SetFireHeld(false); before = f.Shots; f.Step(.5f); f.Step(.5f);
                    Near(before, f.Shots, "a stall creates no firing debt after release");
                }
            }
            var tuning = new MarbleTuning { HoldAccelerationSeconds = 0 };
            bool rejected = false;
            try { tuning.Validate(); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "zero acceleration time is rejected by tuning validation");
            Debug.Log("LAUNCHER_CADENCE_VALIDATION_PASSED: " + (assertions - start) + " assertions.");
        }
    }
}
