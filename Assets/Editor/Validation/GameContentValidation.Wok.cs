using System;
using System.Linq;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static OwnedDeviceData AddWok(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9029));
            CampaignStateUtility.FindDevice(f.C, f.C.Campaign.NextDeviceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void WokUntil(Fixture f, double time)
        { for (int i = 0; f.C.Round.Time + 1e-7 < time && i < 700; i++) f.Step(); }
        private static void WokLuaApi()
        {
            int start = assertions;
            foreach (float speed in new[] { 0f, .03f, 5f })
            using (var f = new Fixture())
            {
                var d = AddWok(f); var key = f.Launch(); var api = CashApi(f, key, d);
                Check(api.Motion.TrySetVelocity(Vector3.right * speed), "set wok entry speed"); api.Close();
                var hit = f.Hit(key, d); f.Step(hit, hit);
                Check(f.C.Manager.HasComponent<HeldMotion>(f.Ball(key)), "wok suspends ball");
                Check(f.Physics.Suspended.Contains(key), "hold suspends physical collisions through bridge");
                Near(0, f.C.BoardRush.Bonus(f.C), "wok bonus starts during hold");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "admission does not spend durability");
                Near(0, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).RushChanceBonus, "global bonus is not copied onto ball");
                double end = f.C.Manager.GetComponentData<HeldMotion>(f.Ball(key)).ReleaseAt;
                for (int i = 0; i < 9; i++) { f.Step(); Check(f.Physics.Suspended.Contains(key), "full one second hold has no early release"); Near(0, f.C.BoardRush.Bonus(f.C), "bonus remains active while cooking"); }
                WokUntil(f, end);
                Check(!f.Physics.Suspended.Contains(key), "release restores dynamic collision mode");
                Near(speed, f.Physics.States[key].LinearVelocity.magnitude, "throw preserves entry speed including zero");
                if (speed > 0)
                    Check(Vector3.Angle(-f.Physics.Poses[d.InstanceId].Forward, f.Physics.States[key].LinearVelocity) <= 60.001f, "random throw stays in upward fan");
                Near(0, f.C.BoardRush.Bonus(f.C), "bonus removed at final release");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "successful throw spends one use");
                CampaignStateUtility.FindDevice(f.C, d.InstanceId, out var entity); double ready = DeviceRoundState.Get(f.C, entity, "readyAt");
                Near(end + 5, ready, "cooldown begins after last release");
                f.Step(hit); Check(!f.Physics.Suspended.Contains(key), "cooling pot lets incoming ball pass");
                Near(0, f.C.BoardRush.Bonus(f.C), "cooldown provides no probability bonus");
                WokUntil(f, ready - .1); f.Step(hit); Check(f.Physics.Suspended.Contains(key), "same ball can trigger again when cooldown ends");
                WokUntil(f, f.C.Round.Time + 1); Near(18, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "next completed release costs a distinct use");
            }
            using (var f = new Fixture())
            {
                var first = AddWok(f); var second = AddWok(f); var a = f.Launch(); var b = f.Launch(); var c = f.Launch();
                f.Step(f.Hit(a, first), f.Hit(b, first), f.Hit(c, second));
                Check(f.Physics.Suspended.Count == 3, "simultaneous entries are independently held");
                Near(0, f.C.BoardRush.Bonus(f.C), "same pot replaces while two pots stack");
                var late = f.Launch(); f.Step(f.Hit(late, first));
                Near(0, f.C.BoardRush.Bonus(f.C), "new ball extends same pot without duplicate contribution");
                double earliest = f.C.Manager.GetComponentData<HeldMotion>(f.Ball(a)).ReleaseAt;
                WokUntil(f, earliest); Near(0, f.C.BoardRush.Bonus(f.C), "other pot expires while later hold keeps first active");
                CampaignStateUtility.FindDevice(f.C, first.InstanceId, out var entity);
                Near(0, DeviceRoundState.Get(f.C, entity, "readyAt"), "cooldown waits for the last held ball");
                WokUntil(f, earliest + .2); Near(0, f.C.BoardRush.Bonus(f.C), "last staggered ball removes remaining bonus");
                Near(17, f.Simulation.GetOwnedDevices().Single(x => x.InstanceId == first.InstanceId).RemainingDurability, "three releases each cost one");
            }
            WokProbability(); WokLifecycleAndApi(); WokPhysics(.01f); WokPhysics(.02f);
            Debug.Log("WOK_VALIDATION_PASSED: " + (assertions - start) + " assertions; per-ball contribution, deferred deposit, cooldown, random throw, wear and collision-free suspension.");
        }
        private static uint WokSeed(double minimum, double maximum)
        {
            for (uint seed = 1; seed < 100000; seed++)
            { uint state = seed; double value = MarbleRules.NextRandom(ref state); if (value > minimum && value < maximum) return seed; }
            throw new Exception("No probability probe seed.");
        }
        private static void WokProbability()
        {
            foreach (var kind in new[] { MarbleContactKind.Score, MarbleContactKind.RandomScore, MarbleContactKind.Drain })
            using (var f = new Fixture())
            {
                var d = AddWok(f); var ball = f.Launch(); f.Step(f.Hit(ball, d));
                Near(17, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(ball)).Contribution, "wok adds ten plus one ordinary device trigger");
                Near(0, f.C.Round.RushMeter, "cooking records contribution without immediate board deposit");
                WokUntil(f, f.C.Round.Time + 1);
                Near(17, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(ball)).Contribution, "release does not count the same stored effect twice");
                f.Step(new MarbleContact { Key=ball, Kind=kind, TargetId=701, Multiplier=1 });
                Near(kind==MarbleContactKind.Drain ? 17 : 25.5, f.C.Round.RushMeter, "wok contribution deposits at terminal zone");
                Near(0, f.C.Round.RushRemaining, "old guaranteed probability does not trigger Rush");
            }
        }
        private static void WokLifecycleAndApi()
        {
            foreach (int limit in new[] { 1, 2, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(x => x.Id == 9029).Durability = limit;
                var d = AddWok(f); var a = f.Launch(); var b = f.Launch(); f.Step(f.Hit(a, d), f.Hit(b, d));
                Near(limit == 1 ? 1 : 2, f.Physics.Suspended.Count, "pending releases reserve remaining wear capacity");
                WokUntil(f, f.C.Round.Time + 1);
                Near(limit == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "last accepted batch finishes and unlimited wear stays infinite");
                Near(0, f.C.BoardRush.Bonus(f.C), "last wear never leaves probability active");
            }
            foreach (bool remove in new[] { false, true })
            using (var f = new Fixture())
            {
                var d = AddWok(f); var key = f.Launch(); var incoming = f.Physics.States[key].LinearVelocity; f.Step(f.Hit(key, d));
                CampaignStateUtility.FindDevice(f.C, d.InstanceId, out var entity);
                if (remove) f.C.Manager.DestroyEntity(entity); else MorePlace(f.C, d.InstanceId, false);
                Near(0, f.C.BoardRush.Bonus(f.C), "removed or recalled source invalidates contribution immediately");
                f.Step(); Check(!f.Physics.Suspended.Contains(key), "cancelled hold restores ball physics");
                VectorNear(incoming, f.Physics.States[key].LinearVelocity, "cancel restores original motion without upward throw");
                if (!remove) Near(0, f.C.Manager.GetComponentData<OwnedDeviceData>(entity).UsesThisRound, "cancelled storage has no wear");
            }
            using (var f = new Fixture())
            {
                var d = AddWok(f); var key = f.Launch(); f.Physics.BlockMoves = true; f.Step(f.Hit(key, d)); WokUntil(f, f.C.Round.Time + 1);
                Check(!f.Physics.Suspended.Contains(key), "blocked outlet still releases in place");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "in-place successful throw costs one use");
                BagBuild(f.C); Check(f.Simulation.BeginRound(), "new round resets cooking state");
                Near(0, f.C.BoardRush.Bonus(f.C), "new round clears probability effects");
                key = f.Launch(); f.Step(f.Hit(key, d)); Check(f.Physics.Suspended.Contains(key), "new round clears five-second cooldown");
                var api = CashApi(f, key, d); var board = api.Board;
                Check(board.SetRushChanceBonus("test", .3, .2), "independent timed probability API accepts named contribution");
                Near(.3, board.RushChanceBonus, "different keys can compose");
                Check(board.SetRushChanceBonus("test", .2, .2), "same source/key replacement succeeds"); Near(.2, board.RushChanceBonus, "replacement does not accumulate");
                Near(f.C.Round.Time + .2, board.GetRushChanceBonusEndTime("test"), "API returns expiry");
                Check(board.RemoveRushChanceBonus("test") && !board.RemoveRushChanceBonus("test"), "remove addresses only source-owned named effect");
                ExpectArgument(() => board.SetRushChanceBonus("", .1, 1)); ExpectArgument(() => board.SetRushChanceBonus("bad", double.NaN, 1));
                ExpectArgument(() => board.SetRushChanceBonus("bad", -.1, 1)); ExpectArgument(() => board.SetRushChanceBonus("bad", 1.1, 1));
                ExpectArgument(() => board.SetRushChanceBonus("bad", .1, 0)); ExpectArgument(() => board.SetRushChanceBonus("bad", .1, 61));
                api.Close(); ExpectExpired(() => { var ignored = board.RushChanceBonus; }); ExpectExpired(() => board.RemoveRushChanceBonus("test"));
                f.Simulation.StartSession(f.Physics); Near(0, f.C.BoardRush.Bonus(f.C), "session reset clears all active probability effects");
            }
        }
    }
}
