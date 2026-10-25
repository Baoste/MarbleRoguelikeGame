using System;
using System.Linq;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static OwnedDeviceData AddCoolingTea(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9030));
            CampaignStateUtility.FindDevice(f.C, f.C.Campaign.NextDeviceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static void CoolingTeaLuaApi()
        {
            int start = assertions;
            foreach (double friction in new[] { 0, .2, .8, 1.0 })
            foreach (double bounce in new[] { 0, .5, 1.5, 2.0 })
            using (var f = new Fixture())
            {
                var first = AddCoolingTea(f); var second = AddCoolingTea(f); var key = f.Launch(); var ball = f.Ball(key);
                AttributeRuntime.SetBase(f.C, ball, GameAttribute.BALL_FRICTION, friction);
                AttributeRuntime.SetBase(f.C, ball, GameAttribute.BALL_BOUNCE, bounce);
                var original = f.Physics.States[key]; var hit = PiggyHit(f, key, first);
                f.Step(hit, hit, PiggyHit(f, key, second));
                double expected = f.C.Content.Definition(GameAttribute.BALL_FRICTION).Clamp(friction * 1.2);
                Near(expected, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_FRICTION), "friction increases twenty percent of current value once");
                Near(bounce * .9, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BOUNCE), "bounce decreases ten percent of current value once");
                Near(5, f.C.Manager.GetComponentData<MarbleRush>(ball).DurationBonus, "ball records a single five-second contribution");
                Near(0, f.C.Round.RushRemaining, "touching tea does not immediately activate or extend Rush");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == first.InstanceId).RemainingDurability, "first effective touch costs one use");
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == second.InstanceId).RemainingDurability, "other teapot cannot reapply or spend wear on same ball");
                f.Step(hit); Near(expected, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_FRICTION), "later contact remains unchanged");
                Near(5, f.C.Manager.GetComponentData<MarbleRush>(ball).DurationBonus, "later contact does not stack duration");
                VectorNear(original.LinearVelocity, f.Physics.States[key].LinearVelocity, "Lua preserves natural solver velocity");
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "tea preserves base value");
                Near(1, f.C.Manager.GetComponentData<MarbleScore>(ball).ScoreMultiplier, "tea preserves score multipliers");
                Check(f.Simulation.TryGetMarble(key, out var snapshot), "tea ball snapshot exists"); Near(5, snapshot.RushDurationBonus, "snapshot exposes deferred Rush seconds");
            }
            TeaRushSettlement(); TeaOnceAndLifetime(); TeaApiAndInheritance(); CoolingTeaPhysics(.01f); CoolingTeaPhysics(.02f);
            Debug.Log("COOLING_TEA_VALIDATION_PASSED: " + (assertions - start) + " assertions; once per marble, physical attribute conversion, conditional Rush extension, caps, inheritance and real collisions.");
        }
        private static void TeaRushSettlement()
        {
            foreach (var kind in new[] { MarbleContactKind.Score, MarbleContactKind.RandomScore, MarbleContactKind.Drain })
            foreach (bool full in new[] { false, true })
            using (var f = new Fixture())
            {
                var d=AddCoolingTea(f); var key=f.Launch(); f.Step(PiggyHit(f,key,d));
                var round=f.C.Round;round.RushMeter=full?665:0;f.C.Round=round;
                f.Step(new MarbleContact{Key=key,Kind=kind,TargetId=710,Multiplier=1});
                Near(full?11:0,f.C.Round.RushRemaining,"tea extends only the Rush filled by this ball");
                Near(full?11:0,f.Simulation.Snapshot.RushDurationSeconds,"HUD uses the extended eleven-second duration");
                Near(full?0:kind==MarbleContactKind.Drain?7:10.5,f.C.Round.RushMeter,"tea keeps normal terminal contribution");
            }
            foreach (bool disabled in new[]{false,true})
            using(var f=new Fixture())
            {
                var d=AddCoolingTea(f);var key=f.Launch();f.Step(PiggyHit(f,key,d));
                var round=f.C.Round;round.RushRemaining=3;round.RushMeter=0;f.C.Round=round;
                f.Step(new MarbleContact{Key=key,Kind=MarbleContactKind.Score,TargetId=711,Multiplier=1,RushDisabled=disabled});
                Near(2.9,f.C.Round.RushRemaining,"active Rush ignores further contributions and tea extensions");
            }
            using(var f=new Fixture())
            {
                var d=AddCoolingTea(f);var key=f.Launch();f.Step(PiggyHit(f,key,d));
                var api=CashApi(f,key,d);Check(api.TrySettleAmount(10),"direct settlement succeeds");api.Close();
                Near(0,f.C.Round.RushRemaining,"direct device settlement does not fill terminal meter");
            }
        }
        private static void TeaOnceAndLifetime()
        {
            foreach (int durability in new[] { 1, 20, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9030).Durability = durability;
                var d = AddCoolingTea(f); var a = f.Launch(); var b = f.Launch();
                var link = PiggyHit(f, a, d); link.LinkKey = 42; f.Step(f.Hit(a, d), link);
                Near(0, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(a)).DurationBonus, "non-impact and link reports are ignored");
                f.Step(PiggyHit(f, a, d), PiggyHit(f, b, d));
                Near(5, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(a)).DurationBonus, "final use applies full effect");
                Near(durability == 1 ? 0 : 5, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(b)).DurationBonus, "simultaneous balls respect remaining wear");
                Near(durability == -1 ? -1 : Math.Max(0, durability - 2), f.Simulation.GetOwnedDevices().Single().RemainingDurability, "effective first touches consume exact wear");
                BagBuild(f.C); Check(f.Simulation.BeginRound(), "new tea round starts"); var fresh = f.Launch();
                Near(0, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(fresh)).DurationBonus, "new ball has no inherited stale Rush state");
                f.Step(PiggyHit(f, fresh, d)); Near(5, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(fresh)).DurationBonus, "new ball can use restored teapot");
            }
            using (var f = new Fixture())
            {
                var d = AddCoolingTea(f); var key = f.Launch();
                f.Simulation.ApplyAttribute(key, 971, GameAttribute.BALL_FRICTION, AttributeOperation.Set, .4, 5);
                f.Simulation.ApplyAttribute(key, 972, GameAttribute.BALL_BOUNCE, AttributeOperation.Multiply, 2, 5);
                AttributeRuntime.SetBase(f.C, f.Ball(key), GameAttribute.BALL_BOUNCE, .4);
                f.Simulation.ApplyAttribute(key, 973, GameAttribute.BALL_MASS, AttributeOperation.Multiply, 2, 5);
                f.Step(PiggyHit(f, key, d)); Near(.48, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_FRICTION), "current friction conversion does not reapply old modifiers");
                Near(.72, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BOUNCE), "current bounce conversion includes existing modifiers exactly once");
                Near(2, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_MASS), "unrelated modifiers remain");
                for (int i = 0; i < 6; i++) f.Step();
                Near(.48, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_FRICTION), "old friction modifier expiration does not undo conversion");
                Near(.72, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BOUNCE), "old bounce modifier expiration does not undo conversion");
                Near(1, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_MASS), "unrelated effect expires normally");
            }
        }
        private static void TeaApiAndInheritance()
        {
            using (var f = new Fixture())
            {
                var tea = AddCoolingTea(f); var key = f.Launch(); var api = CashApi(f, key, tea);
                Near(0, api.RushDurationBonus, "duration API defaults to zero");
                api.RushDurationBonus = 2; Near(2, api.RushDurationBonus, "duration API stores only deferred time");
                ExpectArgument(() => api.RushDurationBonus = -1); ExpectArgument(() => api.RushDurationBonus = double.NaN);
                ExpectArgument(() => api.RushDurationBonus = double.PositiveInfinity); ExpectArgument(() => api.RushDurationBonus = 3601);
                Near(2, api.RushDurationBonus, "invalid writes are atomic");
                Check(api.Visit(-501, 1), "ordinary visit succeeds");
                Check(api.Visit(-502, 1, true), "inheritable visit succeeds");
                Check(!api.Visit(-502, 1, true), "inheritable visit deduplicates same callback"); api.Close();
                ExpectExpired(() => { var ignored = api.RushDurationBonus; }); ExpectExpired(() => api.RushDurationBonus = 0);
                ExpectExpired(() => api.Visit(-503, 1, true));
                f.Step(PiggyHit(f, key, tea)); Near(7, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).DurationBonus, "Lua adds its one-time contribution without overwriting unrelated bonus");
                var copier = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9018));
                var copyApi = CashApi(f, key, copier); var copy = copyApi.CreateCopy();
                Check(copy.TrySpawn(new Vector3(20, 2, 0), Vector3.zero), "generic copy creates descendant"); copyApi.Close();
                var child = f.C.Marbles.Keys.OrderBy(x => x.Sequence).Last();
                Near(7, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(child)).DurationBonus, "copied ball inherits deferred duration");
                var childApi = CashApi(f, child, tea);
                Check(childApi.Visit(-501, 1), "ordinary visits remain fresh on a copy");
                Check(!childApi.Visit(-502, 1, true), "opt-in visit limit survives copying"); childApi.Close();
                f.Step(PiggyHit(f, child, tea)); Near(7, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(child)).DurationBonus, "copied visit history prevents a second tea contribution");
                var portionApi = CashApi(f, key, copier);
                Check(portionApi.CreatePortion(1).TrySpawn(new Vector3(30, 2, 0), Vector3.zero), "value portion creates descendant"); portionApi.Close();
                var portion = f.C.Marbles.Keys.OrderBy(x => x.Sequence).Last();
                f.Step(PiggyHit(f, portion, tea));
                Near(7, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(portion)).DurationBonus, "portion retains bonus and its once-only guard");
                var splitter = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9015));
                f.Step(f.Hit(key, splitter));
                Check(!f.C.Marbles.ContainsKey(key), "split replaces treated parent");
                foreach (var remaining in f.C.Marbles.Keys.ToArray())
                {
                    f.Step(PiggyHit(f, remaining, tea));
                    Near(7, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(remaining)).DurationBonus, "split balls retain full deferred duration rather than dividing it");
                }
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == tea.InstanceId).RemainingDurability, "all descendants preserve effect without repeated wear");
            }
        }
    }
}
