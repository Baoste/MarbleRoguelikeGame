using System;
using System.Linq;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void RushReworkValidation()
        {
            int start=assertions;
            foreach(var kind in new[]{MarbleContactKind.Drain,MarbleContactKind.Score,MarbleContactKind.RandomScore})
            using(var f=new Fixture())
            {
                var key=f.Launch(); Near(2,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).Contribution,"new ball starts with two contribution");
                var device=f.Add(f.C.Balance.Devices.Single(x=>x.Id==9010)); // gold mine
                var api=CashApi(f,key,device); api.MarkTriggered(); api.MarkTriggered(); api.AddRushContribution(10); api.Close();
                Near(17,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).Contribution,"multiple effect APIs in one callback contribute five once");
                f.Step();api=CashApi(f,key,device);api.MarkTriggered();api.Close();
                Near(22,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).Contribution,"later valid trigger on same device adds another five");
                var hit=new MarbleContact{Key=key,Kind=kind,TargetId=800,Multiplier=1,BaseRushChance=1};f.Step(hit,hit);
                Near(kind==MarbleContactKind.Drain?22:33,f.C.Round.RushMeter,"terminal deposit is once-only and score uses one point five");
                Near(0,f.C.Round.RushRemaining,"probability one cannot bypass meter");
                for(int i=0;i<10;i++)f.Step();Near(kind==MarbleContactKind.Drain?22:33,f.C.Round.RushMeter,"meter never decays while idle");
            }
            using(var f=new Fixture())
            {
                var old=f.Launch();var trigger=f.Launch();var round=f.C.Round;round.RushMeter=99;f.C.Round=round;
                f.Step(new MarbleContact{Key=trigger,Kind=MarbleContactKind.Score,TargetId=801,Multiplier=1});
                Near(10,f.C.Round.RushRemaining,"full meter activates ten seconds");Near(0,f.C.Round.RushMeter,"activation consumes the bar including overflow");
                Near(15,RushSystem.BallValue(f.C,f.Ball(old)),"existing ball gets temporary five value");
                Near(10,f.Simulation.GetMarbleAttribute(old,GameAttribute.BALL_BASE_VALUE),"temporary value never changes stored base");
                var player=f.C.Player;player.Blood=0;f.C.Player=player;var fresh=f.Launch();
                Near(0,f.C.Player.Blood,"Rush can fire at zero blood without spending");Near(15,RushSystem.BallValue(f.C,f.Ball(fresh)),"new ball gets same live bonus");
                Near(0,f.Simulation.QuoteShot(0).Cost,"launch preview shows zero blood cost");
                Near(1/f.C.Tuning.FireIntervalSeconds+5,f.Simulation.Snapshot.ShotsPerSecond,"rate adds five shots per second");
                Near(0,f.C.Manager.GetComponentData<BallFeatures>(f.Ball(fresh)).RefundBlood,"free launches do not create blood refunds");
                Near(12,PlayerBloodSystem.Restore(f.C,12),"healing still works and is not doubled");
                f.Step(new MarbleContact{Key=old,Kind=MarbleContactKind.Score,TargetId=802,Multiplier=1});
                Near(25,f.C.Round.Score,"Rush adds five value without old global score multiplier");
                Near(0,f.C.Round.RushMeter,"active Rush pauses deposits");
                double end=f.C.Round.Time+f.C.Round.RushRemaining;WokUntil(f,end);
                Near(0,f.C.Round.RushRemaining,"duration expires by simulation time");Near(10,RushSystem.BallValue(f.C,f.Ball(fresh)),"expiry removes temporary value from surviving balls");
                Near(0,f.C.Round.RushMeter,"next meter starts empty");
            }
            using(var f=new Fixture())
            {
                var d=AddWok(f);var key=f.Launch();var api=CashApi(f,key,d);
                ExpectArgument(()=>api.AddRushContribution(-1));ExpectArgument(()=>api.AddRushContribution(double.NaN));
                ExpectArgument(()=>api.SetScoreRushContribution(double.PositiveInfinity));
                api.SetScoreRushContribution(-100);api.ClearScoreRushContribution();
                Check(!f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).HasScoreContribution,"override can be cleared without changing ordinary contribution");
                var board=api.Board;Near(100,board.RushMaximum,"Lua sees board threshold");api.Close();
                ExpectExpired(()=>api.AddRushContribution(1));ExpectExpired(()=>api.SetScoreRushContribution(1));ExpectExpired(()=>{var x=board.RushMeter;});
            }
            using(var f=new Fixture())
            {
                var key=f.Launch();var round=f.C.Round;round.RushMeter=98;f.C.Round=round;
                var player=f.C.Player;player.Blood=0;f.C.Player=player;f.Step();
                Check(f.C.Round.Phase==(byte)RoundPhase.Draining,"empty blood enters draining outside Rush");
                f.Step(new MarbleContact{Key=key,Kind=MarbleContactKind.Drain,TargetId=803,Multiplier=1});
                Check(f.C.Round.Phase==(byte)RoundPhase.Playing,"last draining ball can fill Rush and reopen free firing");Near(10,f.C.Round.RushRemaining,"drain can start Rush");
            }
            using(var f=new Fixture())
            {
                var d=AddWok(f);var key=f.Launch();var round=f.C.Round;round.RushRemaining=5;f.C.Round=round;
                var api=CashApi(f,key,d);double coins=api.Coins;api.AddCoins(4);api.AddScoreMultiplier(3);
                Near(coins+4,api.Coins,"device coin reward is not doubled during Rush");Near(3,api.DeviceScoreMultiplier,"device multiplier is not doubled during Rush");
                Near(10,api.Value,"device intrinsic value remains independent of temporary Rush bonus");Near(15,api.EffectiveValue,"effective value includes temporary bonus");
                api.SetScoreRushContribution(30);api.SetAppearance("3",1,0,0);api.Close();
                var copyDevice=f.Add(f.C.Balance.Devices.Single(x=>x.Id==9018));var copyApi=CashApi(f,key,copyDevice);
                Check(copyApi.CreateCopy().TrySpawn(new Vector3(30,2,0),Vector3.zero),"copy succeeds during Rush");copyApi.Close();
                var child=f.C.Marbles.Keys.OrderBy(x=>x.Sequence).Last();
                Near(12,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(child)).Contribution,"copy inherits contribution and counts copying once");
                Near(30,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(child)).ScoreContribution,"numbered override survives copy");
                Near(15,RushSystem.BallValue(f.C,f.Ball(child)),"copy does not permanently bake temporary value");
                Check(f.C.Manager.GetComponentData<MarbleAppearance>(f.Ball(child)).Label.ToString()=="3","copy inherits generic appearance");
                var splitDevice=f.Add(f.C.Balance.Devices.Single(x=>x.Id==9015));var splitApi=CashApi(f,key,splitDevice);var batch=splitApi.CreateSplitBatch(3);
                for(int i=0;i<3;i++)Check(batch.TryAdd(new Vector3(40+i*2,2,0),Vector3.zero),"reserve separate split outputs");
                Check(batch.TryCommit(),"split commits while Rush is active");splitApi.Close();f.Step();
                foreach(var k in f.C.Marbles.Keys.Where(x=>!x.Equals(child)))
                {Near(17,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(k)).Contribution,"split descendants carry completed trigger contribution");Near(30,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(k)).ScoreContribution,"split retains numbered override");}
            }
            Debug.Log("RUSH_REWORK_VALIDATION_PASSED: "+(assertions-start)+" assertions.");
        }
        private static void BilliardsValidation()
        {
            int start=assertions;int[] counts=new int[9];
            for(int seed=1;seed<=160;seed++)
            using(var f=new Fixture())
            {
                var d=f.Add(f.C.Balance.Devices.Single(x=>x.Id==9031));var key=f.Launch();
                var round=f.C.Round;round.RandomState=unchecked((uint)seed*747796405u);f.C.Round=round;
                uint random=round.RandomState;int expected=MarbleRules.Roll(ref random,.2)?8:1+(int)(MarbleRules.NextRandom(ref random)*7);
                f.Step(f.Hit(key,d));Check(f.Physics.Suspended.Contains(key),"billiards swallows one ball");
                Near(20,f.Simulation.GetOwnedDevices().Single().RemainingDurability,"swallowing spends no durability");
                var appearance=f.C.Manager.GetComponentData<MarbleAppearance>(f.Ball(key));int number=int.Parse(appearance.Label.ToString());
                Near(expected,number,"Lua uses twenty percent black eight and uniform remaining numbers");counts[number]++;
                Near(number==8?-100:number*10,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).ScoreContribution,"number sets exact terminal contribution override");
                WokUntil(f,f.C.Round.Time+.2);Check(!f.Physics.Suspended.Contains(key),"numbered ball is emitted");
                Near(10,f.Simulation.GetMarbleAttribute(key,GameAttribute.BALL_BASE_VALUE),"numbering preserves value");
                Near(19,f.Simulation.GetOwnedDevices().Single().RemainingDurability,"successful emission spends one wear");
                CampaignStateUtility.FindDevice(f.C,d.InstanceId,out var entity);Near(f.C.Round.Time+15,DeviceRoundState.Get(f.C,entity,"readyAt"),"cooldown begins after emission");
                f.Step(f.Hit(key,d));Check(!f.Physics.Suspended.Contains(key),"cooldown lets ball through");
                round=f.C.Round;round.RushMeter=20;f.C.Round=round;
                f.Step(new MarbleContact{Key=key,Kind=MarbleContactKind.Score,TargetId=804,Multiplier=1});
                Near(number==8?0:20+number*10,f.C.Round.RushMeter,"score override replaces normal contribution and is not multiplied by one point five");
            }
            for(int i=1;i<=8;i++)Check(counts[i]>0,"every numbered outcome is reachable");
            using(var f=new Fixture())
            {
                var d=f.Add(f.C.Balance.Devices.Single(x=>x.Id==9031));var key=f.Launch();var api=CashApi(f,key,d);api.SetScoreRushContribution(-100);api.Close();
                var round=f.C.Round;round.RushRemaining=4;f.C.Round=round;
                f.Step(new MarbleContact{Key=key,Kind=MarbleContactKind.Score,TargetId=805,Multiplier=1});Near(3.9,f.C.Round.RushRemaining,"black eight never interrupts active Rush");
            }
            foreach(int wear in new[]{1,-1})
            using(var f=new Fixture())
            {
                var definition=f.C.Balance.Devices.Single(x=>x.Id==9031);definition.Durability=wear;var d=f.Add(definition);var key=f.Launch();
                f.Physics.BlockMoves=true;f.Step(f.Hit(key,d));uint random=f.C.Round.RandomState;WokUntil(f,f.C.Round.Time+1);
                Check(f.Physics.Suspended.Contains(key),"blocked outlet retries without losing held ball");Near(wear,f.Simulation.GetOwnedDevices().Single().RemainingDurability,"blocked output spends no durability");
                Near(random,f.C.Round.RandomState,"blocked retries do not reroll number");Near(7,f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).Contribution,"blocked retries do not add trigger contribution");
                f.Physics.BlockMoves=false;WokUntil(f,f.C.Round.Time+.3);Check(!f.Physics.Suspended.Contains(key),"last durability completes pending emission");
                Near(wear==-1?-1:0,f.Simulation.GetOwnedDevices().Single().RemainingDurability,"infinite and final use wear are exact");
            }
            BilliardsPhysics(.01f);BilliardsPhysics(.02f);
            Debug.Log("BILLIARDS_VALIDATION_PASSED: "+(assertions-start)+" assertions.");
        }
    }
}
