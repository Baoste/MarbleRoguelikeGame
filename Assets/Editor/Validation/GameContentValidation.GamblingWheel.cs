using System;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static uint WheelSeed(int index)
        {
            for (uint seed = 1; seed < 100000; seed++)
            {
                uint state = seed;
                if (GamblingWheelRules.Outcome(MarbleRules.NextRandom(ref state)) == GamblingWheelRules.Multiplier(index)) return seed;
            }
            throw new Exception("No seed for wheel sector");
        }

        private static void GamblingWheelValidation()
        {
            int start = assertions;
            int[] outcomes = { -2, -1, 0, 1, 5, 10, 20, 50 };
            for (int i = 0; i < outcomes.Length; i++)
            {
                Near(outcomes[i], GamblingWheelRules.Outcome(i / 8d), "sector lower inclusive boundary");
                Near(outcomes[i], GamblingWheelRules.Outcome((i + 1) / 8d - 1e-12), "sector upper exclusive boundary");
                using (var f = new Fixture())
                {
                    var round = f.C.Round; round.Score = 100; round.RandomState = WheelSeed(i); f.C.Round = round;
                    uint expected = round.RandomState; MarbleRules.NextRandom(ref expected);
                    GamblingWheelSystem.Deposit(f.C, 10.25);
                    Near(100, f.C.Round.Score, "entry does not settle immediately");
                    Check(GamblingWheelSystem.Draw(f.C), "nonempty pool draws");
                    Near(outcomes[i], f.C.Round.WheelLastMultiplier, "runtime outcome");
                    Near(100 + 10.25 * outcomes[i], f.C.Round.Score + f.C.Round.FractionalScore, "pool fractions preserved through draw");
                    Check(expected == f.C.Round.RandomState, "one draw advances only shared RNG");
                    Near(0, f.Simulation.GamblingWheel.PoolValue, "pool clears");
                    Check(!GamblingWheelSystem.Draw(f.C) && expected == f.C.Round.RandomState, "empty draw is idempotent");
                    Near(1, f.C.Round.WheelDrawCount, "one committed result");
                }
            }
            using (var f = new Fixture())
            {
                uint state = f.C.Round.RandomState;
                for (int i = 1; i <= 100; i++)
                {
                    GamblingWheelSystem.Deposit(f.C, .125);
                    Near(Math.Max(.2, 2 / Math.Sqrt(i)), f.Simulation.GamblingWheel.SecondsRemaining, "window shortens per entry");
                }
                Near(12.5, f.Simulation.GamblingWheel.PoolValue, "fractional entries sum before rounding");
                Check(state == f.C.Round.RandomState, "entries do not draw");
                var round = f.C.Round; round.Time = round.WheelExpiresAt - .00001; f.C.Round = round;
                GamblingWheelSystem.Tick(f.C);
                Near(0, f.C.Round.WheelDrawCount, "not before deadline");
                round = f.C.Round; round.Time = round.WheelExpiresAt; f.C.Round = round;
                GamblingWheelSystem.Deposit(f.C, 3);
                Near(1, f.C.Round.WheelDrawCount, "deadline entry first resolves old pool");
                Near(12.5, f.C.Round.WheelLastPool, "old pool excludes new entry");
                Near(3, f.Simulation.GamblingWheel.PoolValue, "deadline entry opens new pool");
                Near(2, f.Simulation.GamblingWheel.SecondsRemaining, "new pool resets count and duration");
                f.Step();
                Check(f.Simulation.GamblingWheel.SecondsRemaining < 2, "fixed steps advance wheel timer");
            }
            using (var f = new Fixture())
            {
                var round = f.C.Round; round.Score = 3; round.FractionalScore = .5; round.RandomState = WheelSeed(0); f.C.Round = round;
                long coins = f.C.Campaign.Coins; float blood = f.C.Player.Blood;
                GamblingWheelSystem.Deposit(f.C, 10); GamblingWheelSystem.Draw(f.C);
                Near(0, f.C.Round.Score + f.C.Round.FractionalScore, "loss floors current round score at zero");
                Near(-3, f.C.Round.WheelLastScoreChange, "display reports actual integer deduction");
                Near(coins, f.C.Campaign.Coins, "loss does not spend coins");
                Near(blood, f.C.Player.Blood, "loss does not spend blood");
                Near(0, f.C.Campaign.TotalScore, "loss does not touch campaign total");
                GamblingWheelSystem.Deposit(f.C, 0); GamblingWheelSystem.Draw(f.C);
                Near(2, f.C.Round.WheelDrawCount, "zero value ball still opens and draws a pool");
                round = f.C.Round; round.Score = f.C.Tuning.MaxRoundScore - 1; round.RandomState = WheelSeed(7); f.C.Round = round;
                GamblingWheelSystem.Deposit(f.C, f.C.Tuning.MaxRoundScore); GamblingWheelSystem.Draw(f.C);
                Near(f.C.Tuning.MaxRoundScore, f.C.Round.Score, "large payout saturates safely");
            }
            using (var f = new Fixture())
            {
                var key = f.Launch();
                var score = f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)); score.ScoreMultiplier = 3;
                f.C.Manager.SetComponentData(f.Ball(key), score);
                var contact = new MarbleContact { Key = key, TargetId = 9, Kind = MarbleContactKind.RandomScore,
                    Multiplier = 2, ScoreStackEnabled = true };
                f.Step(contact, contact);
                Near(61.5, f.Simulation.GamblingWheel.PoolValue, "entry includes value, zone, device and score-stack multipliers");
                Near(1, f.Simulation.GamblingWheel.BallCount, "duplicate terminal contacts count once");
                Near(3, f.C.Round.RushMeter, "gambling entry still deposits RUSH at 1.5x");
                var second = f.Launch();
                f.Step(new MarbleContact { Key = second, TargetId = 10, Kind = MarbleContactKind.RandomScore, Multiplier = 1 });
                Near(2, f.Simulation.GamblingWheel.BallCount, "different gambling zones share one pool");
                f.C.RemoveAllMarbles();
                var round = f.C.Round; round.RandomState = WheelSeed(0); round.Score = round.TargetScore; round.Phase = (byte)RoundPhase.Draining; f.C.Round = round;
                RoundFlowSystem.Execute(f.C);
                Check(f.C.Round.SettlementComplete && f.C.Round.Phase == (byte)RoundPhase.Lost, "final draw occurs before campaign pass/fail");
                Near(1, f.C.Round.WheelDrawCount, "round end flushes pool exactly once");
                RoundFlowSystem.Execute(f.C);
                Near(1, f.C.Round.WheelDrawCount, "repeated finalization does not draw again");
                f.Simulation.StartSession(f.Physics); f.Simulation.BeginRound();
                Near(0, f.Simulation.GamblingWheel.DrawCount, "restart clears results");
                Near(0, f.Simulation.GamblingWheel.PoolValue, "restart clears pool");
            }
            using (var sim = new MarbleSimulation(Balance().Marble))
            {
                var physics = new FakePhysics(); sim.StartRound(physics);
                GamblingWheelSystem.Deposit(sim.Context, 10);
                var round = sim.Context.Round; round.RandomState = WheelSeed(4); round.Phase = (byte)RoundPhase.Draining; sim.Context.Round = round;
                RoundFlowSystem.Execute(sim.Context);
                Near(50, sim.Context.Round.Score, "standalone mode also draws before ending");
            }
            Debug.Log("GAMBLING_WHEEL_VALIDATION_PASSED: " + (assertions - start) + " assertions.");
        }
    }
}
