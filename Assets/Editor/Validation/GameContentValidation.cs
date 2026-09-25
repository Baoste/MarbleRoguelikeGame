using System;
using System.Collections.Generic;
using System.Linq;
using MarblesECS.PhysX;
using Unity.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    [InitializeOnLoad]
    public static class GameContentValidation
    {
        private static int assertions;
        static GameContentValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("MarbleContentValidation.Pending", false))
                {
                    SessionState.SetBool("MarbleContentValidation.Pending", false);
                    EditorApplication.delayCall += Run;
                }
            };
        }
        [MenuItem("Marbles ECS/Validate Attribute and Content Runtime")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                if (Application.isBatchMode) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionState.SetBool("MarbleContentValidation.Pending", true);
                EditorApplication.EnterPlaymode();
                return;
            }
            try
            {
                assertions = 0;
                Rules();
                ContentAuthority();
                ContentLimitAuthority();
                BounceAuthority();
                Placement();
                DevicePlacementValidation.Run();
                SpawnAndDrugs();
                LegacyContent();
                Devices();
                EconomyAndGambling();
                Boundaries();
                PhysicalBridge();
                Debug.Log("CONTENT_VALIDATION_PASSED: " + assertions + " assertions.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }
        private static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new Exception("Content validation: " + message); }
        private static void Near(double expected, double actual, string message)
        { Check(Math.Abs(expected - actual) < .0001, message + " expected " + expected + ", actual " + actual); }
        private static GameBalance Balance()
        {
            var b = GameBalanceLoader.Load();
            b.Marble.InitialBlood = b.Marble.BloodCapacity = 1000;
            b.Marble.MinInvestment = b.Marble.MaxInvestment = 1;
            b.Content.Definition(GameAttribute.BALL_BASE_VALUE).DefaultValue = 10;
            b.Marble.FireIntervalSeconds = .01f;
            foreach (var d in b.Devices) d.StartingCount = 0;
            foreach (var d in b.Drugs) d.StartingCount = 5;
            b.Campaign.MaxDrugInventory = 100;
            b.Validate(); return b;
        }
        private static void Rules()
        {
            var b = Balance(); var content = b.Content;
            Check(content.Attributes.Length == 34 && content.Devices.Length == 15 && content.Drugs.Length == 12, "catalog coverage including migrated legacy definitions");
            foreach (var a in content.Attributes) a.Validate();
            var effects = new[] { new AttributeEffect { Attribute = GameAttribute.BLOOD_COST, Operation = AttributeOperation.Add, Value = 1 },
                new AttributeEffect { Attribute = GameAttribute.BLOOD_COST, Operation = AttributeOperation.Multiply, Value = 2 } };
            Near(7, AttributeMath.Evaluate(content.Definition(GameAttribute.BLOOD_COST), 3, effects), "cost MUL before ADD");
            for (int i = 0; i < effects.Length; i++) effects[i].Attribute = GameAttribute.BALL_BASE_VALUE;
            Near(8, AttributeMath.Evaluate(content.Definition(GameAttribute.BALL_BASE_VALUE), 3, effects), "value ADD before MUL");
            Check(AttributeMath.GamblingOutcome(.1,.25,.25,.25,.25)==0, "loss");
            Check(AttributeMath.GamblingOutcome(.3,.25,.25,.25,.25)==1, "refund");
            Check(AttributeMath.GamblingOutcome(.6,.25,.25,.25,.25)==2, "double");
            Check(AttributeMath.GamblingOutcome(.9,.25,.25,.25,.25)==4, "quadruple");
            Near(2, content.Definition(GameAttribute.BALL_BOUNCE).Clamp(3.6), "bounce cap");
            Near(3, content.Definition(GameAttribute.BALL_SPAWN_COUNT).Clamp(3.9), "count floor");
            bool rejected = false;
            try { AttributeMath.Evaluate(content.Definition(GameAttribute.BALL_SPLIT_COUNT), 2, new[] {
                new AttributeEffect { Attribute = GameAttribute.BALL_SPLIT_COUNT, Operation = AttributeOperation.Multiply, Value = 2 } }); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "unsupported operation rejected");
        }
        private static void ContentAuthority()
        {
            var balance = Balance();
            balance.Content.Definition(GameAttribute.BALL_RADIUS).DefaultValue = .75;
            balance.Content.Definition(GameAttribute.BALL_MASS).DefaultValue = 2;
            balance.Content.Definition(GameAttribute.BALL_BASE_VALUE).DefaultValue = 23;
            balance.Content.Definition(GameAttribute.BALL_BOUNCE).DefaultValue = .4;
            balance.Content.Definition(GameAttribute.BALL_LAUNCH_SPEED).DefaultValue = 3;
            balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue = 4;
            balance.Content.Definition(GameAttribute.BLOOD_COST).DefaultValue = 1.5;
            balance.Content.Definition(GameAttribute.BLOOD_DIVIDEND_RATE).DefaultValue = 1.25;
            balance.Content.Definition(GameAttribute.COIN_GAIN_RATE).DefaultValue = 1.1;
            balance.Marble.MinInvestment = 1; balance.Marble.MaxInvestment = 5;
            // Deliberately disagree with every obsolete tuning input.
            balance.Marble.MinRadius = .24f; balance.Marble.MaxRadius = .36f;
            balance.Marble.MassPerBlood = 7; balance.Marble.BaseScore = 999;
            balance.Marble.BloodCostMultiplier = 4; balance.Marble.LaunchSpeed = 8;
            balance.Marble.LauncherScoreMultiplier = 6; balance.Marble.BaseRestitution = .9f;
            balance.Marble.MaxLifeSeconds = 70; balance.Campaign.CashoutCoinsPerBlood = 3.5;
            balance.Stages[0].CashoutRate = 1.2;
            var copied = balance.Copy();
            Check(ReferenceEquals(copied.Devices, copied.Content.Devices) && ReferenceEquals(copied.Drugs, copied.Content.Drugs), "catalogs share the copied content source");
            Check(!ReferenceEquals(copied.Content.Devices, balance.Content.Devices), "session catalog remains isolated from authoring");
            var physics = new FakePhysics();
            using (var simulation = new MarbleSimulation(balance))
            {
                simulation.StartSession(physics); simulation.BeginRound();
                var low = simulation.QuoteShot(0); var high = simulation.QuoteShot(1);
                Near(.12, low.Radius, "radius comes only from content and unit conversion");
                Near(low.Radius, high.Radius, "flow no longer scales radius");
                Near(2, high.Mass, "flow and old MassPerBlood cannot multiply content mass");
                Near(23, high.BaseValue, "old BaseScore cannot multiply content base value");
                Near(1, high.LauncherMultiplier, "old launcher multiplier is neutral in content mode");
                Near(1.5, low.Cost, "content blood cost per investment unit");
                Near(7.5, high.Cost, "investment remains the explicit player input");
                simulation.FireOnce(); simulation.BeforePhysics(.1f, physics, Vector3.up * 2, Quaternion.identity); simulation.AfterPhysics(physics);
                var spawn = physics.Spawns.Single().Value;
                Near(3, spawn.LinearVelocity.magnitude, "launch speed is absolute content speed");
                Near(.4, spawn.Settings.Bounce, "content bounce overrides obsolete restitution");
                Near(4, simulation.GetMarbleAttribute(spawn.Key, GameAttribute.BALL_LIFETIME), "content lifetime");
                var c = simulation.Context; var player = c.Player; player.Blood = 100; c.Player = player;
                var round = c.Round; round.Score = round.TargetScore; c.Round = round;
                Check(simulation.CashOut(), "content cashout accepted");
                Near(165, c.Campaign.LastCashoutCoins, "cashout uses content dividend, stage rate and coin gain exactly once");
            }
            using (var standalone = new MarbleSimulation(balance.Marble, balance.Content))
            {
                var standalonePhysics = new FakePhysics(); standalone.StartRound(standalonePhysics);
                Near(.12, standalone.QuoteShot(1).Radius, "standalone scenes use content radius too");
                standalone.FireOnce(); standalone.BeforePhysics(.1f, standalonePhysics, Vector3.up * 2, Quaternion.identity); standalone.AfterPhysics(standalonePhysics);
                Near(3, standalonePhysics.Spawns.Single().Value.LinearVelocity.magnitude, "standalone launch reads content");
            }
            string output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MarbleBalanceAuthority.json");
            GameBalanceImporter.Import(GameBalanceImporter.WorkbookPath, output);
            string exported = System.IO.File.ReadAllText(output);
            foreach (string field in new[] { "MinRadius", "MaxRadius", "MassPerBlood", "BaseScore", "BloodCostMultiplier", "LaunchSpeed", "LauncherScoreMultiplier", "MaxLifeSeconds", "BaseRestitution", "RushDurationSeconds", "CashoutCoinsPerBlood", "GamblingWinChance", "GamblingWinMultiplier", "Devices", "Drugs", "Board", "Zones" })
                Check(!exported.Contains("\"" + field + "\""), "base import cannot restore " + field);
            var legacyJson = new TextAsset(exported.Replace("\"Marble\": {", "\"Marble\": {\"MinRadius\":9,\"BaseScore\":999,"));
            try
            {
                var loaded = GameBalanceLoader.Load(legacyJson);
                using (var fromJson = new MarbleSimulation(loaded))
                {
                    Near(loaded.Content.Definition(GameAttribute.BALL_BASE_VALUE).DefaultValue, fromJson.QuoteShot(0).BaseValue,
                        "obsolete JSON base score cannot change the quote");
                    Near(loaded.Content.Definition(GameAttribute.BALL_RADIUS).DefaultValue * AttributeRuntime.RadiusToWorld, fromJson.QuoteShot(0).Radius,
                        "obsolete JSON radius cannot change the quote");
                }
                Check(loaded.Marble.MinRadius != 9 && loaded.Marble.BaseScore != 999, "obsolete JSON fields are ignored");
            }
            finally { UnityEngine.Object.DestroyImmediate(legacyJson); }
        }

        private static void ContentLimitAuthority()
        {
            foreach (bool pending in new[] { false, true })
            using (var f = new Fixture())
            {
                f.C.Content.Definition(GameAttribute.MACHINE_MULT).MaxValue = 7;
                f.C.Content.Definition(GameAttribute.GLOBAL_MULT).MaxValue = 9;
                f.C.Content.Definition(GameAttribute.GAMBLE_POT_VALUE).MaxValue = 3;
                f.C.Content.Definition(GameAttribute.RUSH_DURATION).MaxValue = 240;
                f.Simulation.ApplyGlobalAttribute(1, GameAttribute.MACHINE_MULT, AttributeOperation.Set, 7);
                f.Simulation.ApplyGlobalAttribute(2, GameAttribute.GLOBAL_MULT, AttributeOperation.Set, 9);
                f.Simulation.ApplyGlobalAttribute(3, GameAttribute.BALL_GAMBLE_VALUE_MOD, AttributeOperation.Set, 2);
                f.Simulation.ApplyGlobalAttribute(4, GameAttribute.GAMBLE_POT_VALUE, AttributeOperation.Set, 3);
                f.Simulation.ApplyGlobalAttribute(5, GameAttribute.RUSH_DURATION, AttributeOperation.Set, 180);
                var player = f.C.Player; player.DrugScoreMultiplier = 2; f.C.Player = player;
                var key = f.Launch();
                f.Step(new MarbleContact { Key = key, TargetId = 1,
                    Kind = pending ? MarbleContactKind.RandomScore : MarbleContactKind.Score,
                    Multiplier = 50, BaseRushChance = 1 });
                Near(pending ? 1890 : 630, pending ? f.C.Round.PendingScore : f.C.Round.Score,
                    "combined multipliers respect authored caps, pending=" + pending);
                Near(180, f.C.Round.RushRemaining, "authored Rush cap can exceed the old 120-second constant");
            }
        }

        private static void BounceAuthority()
        {
            foreach (float bounce in new[] { 0f, .5f, 1.5f })
            {
                var scene = SceneManager.CreateScene("Bounce authority " + bounce, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var wall = new GameObject("High bounce wall");
                SceneManager.MoveGameObjectToScene(wall, scene);
                var collider = wall.AddComponent<BoxCollider>(); collider.size = new Vector3(.2f, 4, 4);
                var material = new PhysicMaterial { bounciness = 1, bounceCombine = PhysicMaterialCombine.Maximum };
                collider.sharedMaterial = material;
                var bridge = new MarblePhysicsBridge(scene, null, material, _ => { });
                try
                {
                    var key = new MarbleKey(1, 1);
                    Check(bridge.TrySpawn(new MarbleSpawnData { Key = key, Position = Vector3.left, Rotation = Quaternion.identity,
                        Quote = new ShotQuote(1, 1, .08f, 1, 10, 1), LinearVelocity = Vector3.right * 3,
                        HasRestitution = true, Restitution = Mathf.Min(1, bounce), HasSettings = true,
                        Settings = new MarblePhysicalSettings { Radius = .08f, Mass = 1, Bounce = bounce, Gravity = 0, Friction = 0, MaxSpeed = 20 } }), "bounce authority spawn");
                    for (int i = 0; i < 25; i++) bridge.Simulate(.02f);
                    bridge.TryGetState(key, out var state);
                    Check(Math.Abs(state.LinearVelocity.x + 3 * bounce) < .2, "wall material cannot override BALL_BOUNCE=" + bounce);
                }
                finally
                {
                    bridge.Dispose(); UnityEngine.Object.DestroyImmediate(wall); UnityEngine.Object.DestroyImmediate(material);
                    SceneManager.UnloadSceneAsync(scene);
                }
            }
        }

        private static void Placement()
        {
            using (var simulation = new MarbleSimulation(GameBalance.Default()))
            {
                simulation.StartSession(new FakePhysics());
                var devices = simulation.GetOwnedDevices();
                Check(simulation.PlaceDevice(devices[0].InstanceId, 0, 3.7f), "old generated pin coordinate is no longer reserved");
                float separation = devices[0].Radius + devices[1].Radius + .05f;
                int revision = simulation.Session.LayoutRevision;
                Check(!simulation.PlaceDevice(devices[1].InstanceId, separation, 3.7f, .1f), "Inspector clearance rejects close devices");
                Check(simulation.Session.LayoutRevision == revision, "rejected placement leaves layout unchanged");
                Check(simulation.PlaceDevice(devices[1].InstanceId, separation, 3.7f, 0), "changed clearance reaches spacing rule");
                Check(!simulation.PlaceDevice(devices[1].InstanceId, 0, 3.7f, 0), "overlapping owned devices remain rejected");
                Check(!simulation.PlaceDevice(devices[1].InstanceId, 2, 3.7f, float.NaN), "invalid clearance is rejected");
                simulation.ConfigureLauncherMovement(new Vector3(5, 0, 9), 2, 1);
                simulation.BeginRound();
                simulation.RequestLauncherPosition(100);
                simulation.StepLauncherMovement(.02f);
                Near(11, simulation.LauncherLocalPosition.z, "launcher range uses scene starting position");
                Near(9, simulation.LauncherCenterZ, "HUD center remains fixed when launcher moves");
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly FakePhysics Physics = new FakePhysics();
            internal readonly MarbleSimulation Simulation;
            internal SimulationContext C => Simulation.Context;
            internal Fixture()
            {
                Simulation = new MarbleSimulation(Balance());
                Simulation.StartSession(Physics); Simulation.BeginRound();
            }
            internal MarbleKey Launch()
            {
                Simulation.FireOnce(); Step();
                return C.Marbles.Keys.OrderBy(x => x.Sequence).Last();
            }
            internal void Step(params MarbleContact[] contacts)
            {
                Simulation.BeforePhysics(.1f, Physics, new Vector3(0,2,0), Quaternion.identity);
                foreach (var contact in contacts) Simulation.EnqueueContact(contact);
                Simulation.AfterPhysics(Physics);
            }
            internal OwnedDeviceData Add(DeviceKind kind)
            {
                var definition = C.Balance.Devices.Single(x => x.Kind == kind);
                CampaignStateUtility.CreateDevice(C, definition, definition.Price);
                var snapshots = Simulation.GetOwnedDevices().Where(x => x.DefinitionId == definition.Id).ToArray();
                foreach (var snapshot in snapshots)
                {
                    CampaignStateUtility.FindDevice(C, snapshot.InstanceId, out var e);
                    var d = C.Manager.GetComponentData<OwnedDeviceData>(e); d.Placed = true;
                    C.Manager.SetComponentData(e,d);
                    Physics.Poses[d.InstanceId] = new DevicePose { Position = new Vector3(d.InstanceId,2,0),
                        Forward = Vector3.forward, Up = Vector3.up, Radius = definition.Radius };
                }
                CampaignStateUtility.FindDevice(C,snapshots[0].InstanceId,out var entity);
                return C.Manager.GetComponentData<OwnedDeviceData>(entity);
            }
            internal MarbleContact Hit(MarbleKey key, OwnedDeviceData d) => new MarbleContact {
                Key=key,TargetId=100000+d.InstanceId,Kind=MarbleContactKind.Device,Multiplier=1,Position=Vector3.zero };
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            public void Dispose() { Simulation.Dispose(); }
        }
        private static void SpawnAndDrugs()
        {
            using (var f = new Fixture())
            {
                f.Simulation.ApplyGlobalAttribute(1,GameAttribute.BALL_SPAWN_COUNT,AttributeOperation.Set,3);
                f.Physics.FailSpawnAt = 2;
                f.Simulation.FireOnce(); f.Step();
                Near(1000,f.C.Player.Blood,"batch failure blood rollback");
                Check(f.Physics.Spawns.Count==0 && f.C.Marbles.Count==0,"batch failure body/entity rollback");
                f.Physics.FailSpawnAt=0; f.Step();
                Check(f.C.Marbles.Count==3,"batch count");
                Near(997,f.C.Player.Blood,"batch costs each ball");
            }
            foreach (var kind in Enum.GetValues(typeof(DrugKind)).Cast<DrugKind>().Where(x=>x!=DrugKind.LegacyBounce))
            using (var f = new Fixture())
            {
                var before=f.Launch();
                var definition=f.C.Balance.Drugs.Single(x=>x.Kind==kind);
                Check(f.Simulation.UseDrug(definition.Id),"use drug "+kind);
                var after=f.Launch();
                Near(10,f.Simulation.GetMarbleAttribute(before,GameAttribute.BALL_BASE_VALUE),"old ball snapshot "+kind);
                var features=f.C.Manager.GetComponentData<BallFeatures>(f.Ball(after));
                if(kind==DrugKind.Adrenaline) Near(.2,features.RushChanceAdd,"adrenaline");
                if(kind==DrugKind.RushFruit) Near(1.15,features.RushScoreMultiplier,"rush fruit");
                if(kind==DrugKind.RushDuration) Near(12,f.Simulation.GetMarbleAttribute(after,GameAttribute.RUSH_DURATION),"duration");
                if(kind==DrugKind.Lubricant) Near(.15,f.Simulation.GetMarbleAttribute(after,GameAttribute.BALL_FRICTION),"lubricant");
                if(kind==DrugKind.Coagulant) Check(f.Physics.Spawns[after].Quote.Radius>f.Physics.Spawns[before].Quote.Radius,"volume radius conversion");
                if(kind==DrugKind.Ecstasy) Near(2,f.Simulation.GetMarbleAttribute(after,GameAttribute.BALL_BOUNCE),"ecstasy clamp");
                if(kind==DrugKind.ThinBlood) Near(.3,features.PierceChance,"thin blood");
                if(kind==DrugKind.Replicator) Check(features.Replicates,"replicator inherited flag");
                if(kind==DrugKind.Magnetic) Check(features.Magnetic,"magnetic tag");
                if(kind==DrugKind.Direction) Check(features.ExitDown,"direction tag");
                Check(f.Simulation.UseDrug(definition.Id),"reuse drug");
                Check(f.Simulation.GetDrugInventory().Single(x=>x.DefinitionId==definition.Id).ActiveDoses==(definition.Stackable?2:1),"stack policy "+kind);
            }
        }
        private static void LegacyContent()
        {
            foreach (uint id in new uint[] { 1, 2 })
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Drugs.Single(x => x.Id == id);
                var before = f.Launch();
                double original = f.Simulation.GetMarbleAttribute(before, GameAttribute.BALL_BOUNCE);
                Check(f.Simulation.UseDrug(id), "use migrated bounce drug " + id);
                var after = f.Launch();
                Near(original * definition.RestitutionMultiplier, f.Physics.Spawns[after].Settings.Bounce, "legacy drug reaches physics " + id);
                Near(original, f.Simulation.GetMarbleAttribute(before, GameAttribute.BALL_BOUNCE), "legacy drug preserves old ball " + id);
                var inventory = f.Simulation.GetDrugInventory().Single(x => x.DefinitionId == id);
                Check(inventory.Count == 4 && inventory.ActiveDoses == 1 && inventory.SecondsRemaining > 0, "legacy drug UI inventory and timer " + id);
                Check(f.Simulation.UseDrug(id) && f.Simulation.GetDrugInventory().Single(x => x.DefinitionId == id).ActiveDoses == 2, "legacy doses stack independently " + id);
                for (int i = 0; i < 130; i++) f.Step();
                Check(f.Simulation.GetDrugInventory().Single(x => x.DefinitionId == id).ActiveDoses == 0, "legacy drug window expires " + id);
                var expired = f.Launch();
                Near(original, f.Physics.Spawns[expired].Settings.Bounce, "expired legacy drug does not affect new balls " + id);
                Near(original * definition.RestitutionMultiplier, f.Simulation.GetMarbleAttribute(after, GameAttribute.BALL_BOUNCE), "legacy ball keeps launch snapshot after expiry " + id);
            }
            using (var f = new Fixture())
            {
                f.C.Tuning.MaxActiveBounceDrugs = 1;
                Check(f.Simulation.UseDrug(1) && !f.Simulation.UseDrug(2), "legacy shared dose capacity");
                Check(f.Simulation.GetDrugInventory().Single(x => x.DefinitionId == 2).Count == 5, "rejected legacy dose does not consume inventory");
            }
            using (var f = new Fixture())
            {
                var key = f.Launch();
                foreach (var definition in f.C.Balance.Devices.Where(x => x.Kind == DeviceKind.LegacyPin))
                {
                    var contact = new MarbleContact { Key = key, TargetId = 100000 + (int)definition.Id,
                        Kind = definition.ScoreMultiplier == 1 ? MarbleContactKind.RushPin : MarbleContactKind.Multiplier,
                        Multiplier = definition.ScoreMultiplier, RushChanceAdd = definition.RushChanceAdd };
                    f.Step(contact); f.Step(contact);
                }
                Near(2, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(key)).ScoreMultiplier, "legacy multiplier pin applies once");
                Near(.08, f.C.Manager.GetComponentData<MarbleRush>(f.Ball(key)).RushChanceBonus, "legacy rush pin applies once");
                f.Step(f.Hit(key, f.Add(DeviceKind.Lens)));
                f.Step(new MarbleContact { Key = key, TargetId = 10, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true });
                Near(40, f.C.Round.Score, "legacy multiplier combines with new lens");
            }
        }
        private static void Devices()
        {
            foreach(var kind in Enum.GetValues(typeof(DeviceKind)).Cast<DeviceKind>().Where(x=>x!=DeviceKind.LegacyPin && x!=DeviceKind.Magnet))
            using(var f=new Fixture())
            {
                var device=f.Add(kind); var key=f.Launch(); var ball=f.Ball(key);
                var contact=f.Hit(key,device);
                if(kind==DeviceKind.Amplifier) contact.LinkKey=((long)device.InstanceId<<32)|99;
                double coins=f.C.Campaign.Coins;
                f.Step(contact);
                switch(kind)
                {
                    case DeviceKind.Bank:
                        Check(!f.C.Marbles.ContainsKey(key),"bank consumes");
                        Near(coins+1,f.C.Campaign.Coins,"bank 10*.12 floors once"); break;
                    case DeviceKind.Amplifier: Near(12,f.Simulation.GetMarbleAttribute(key,GameAttribute.BALL_BASE_VALUE),"tower value"); break;
                    case DeviceKind.Lens:
                        Near(20,f.Simulation.GetMarbleAttribute(key,GameAttribute.BALL_BASE_VALUE),"lens");
                        f.Step();f.Step(contact);Near(40,f.Simulation.GetMarbleAttribute(key,GameAttribute.BALL_BASE_VALUE),"repeat lens");break;
                    case DeviceKind.Capital: Near(30,f.Simulation.GetMarbleAttribute(key,GameAttribute.BALL_BASE_VALUE),"capital");break;
                    case DeviceKind.Clover: Check(f.C.Manager.GetComponentData<BallFeatures>(ball).Clover,"clover marker");break;
                    case DeviceKind.Revive: Check(f.C.Manager.GetComponentData<BallFeatures>(ball).Revive,"revive marker");break;
                    case DeviceKind.BloodCannon: Check(f.Physics.CannonShots==1 && !CampaignStateUtility.FindDevice(f.C,device.InstanceId,out _),"cannon consumed");break;
                    case DeviceKind.Splitter:
                        Check(f.C.Marbles.Count==2,"two split children");
                        Near(10,f.C.Marbles.Keys.Sum(k=>f.Simulation.GetMarbleAttribute(k,GameAttribute.BALL_BASE_VALUE)),"split conserved value");
                        Check(f.C.Marbles.Values.All(e=>f.C.Manager.GetComponentData<BallFeatures>(e).Generation==1),"split generation");break;
                    case DeviceKind.Centrifuge: Check(f.Physics.Moves==1,"centrifuge moves");break;
                    case DeviceKind.Paddle: Check(f.Physics.Moves==1,"paddle moves");break;
                    case DeviceKind.Portal:
                        Check(f.Physics.Moves==1,"portal moves");
                        Check(f.C.Manager.GetComponentData<BallFeatures>(ball).PortalLockedUntil>f.C.Round.Time,"portal global lock");break;
                    case DeviceKind.Slow: Near(.7,f.Physics.States[key].LinearVelocity.magnitude/f.Physics.Spawns[key].LinearVelocity.magnitude,"slow");break;
                }
            }
            using(var f=new Fixture())
            {
                var key=f.Launch(); var device=f.Add(DeviceKind.Revive); f.Step(f.Hit(key,device));
                f.Step(new MarbleContact { Key=key,TargetId=1,Kind=MarbleContactKind.Drain,Multiplier=0 });
                Near(1000,f.C.Player.Blood,"revive refunds on drain");
            }
            using(var f=new Fixture())
            {
                f.Simulation.UseDrug(f.C.Balance.Drugs.Single(x=>x.Kind==DrugKind.Replicator).Id);
                var key=f.Launch();
                f.Step(new MarbleContact{Key=key,TargetId=42,Kind=MarbleContactKind.Pin,Multiplier=1});
                Check(f.C.Marbles.Count==2,"pin replication");
                Check(f.C.Marbles.Values.All(e=>f.C.Manager.GetComponentData<BallFeatures>(e).Replicates),"recursive clone inheritance");
            }
        }
        private static void EconomyAndGambling()
        {
            foreach(int outcome in new[]{0,1,2,4})
            using(var f=new Fixture())
            {
                foreach(var attribute in new[]{GameAttribute.GAMBLE_LOSS_CHANCE,GameAttribute.GAMBLE_RETURN_CHANCE,GameAttribute.GAMBLE_WIN_CHANCE,GameAttribute.GAMBLE_QUADRUPLE_CHANCE})
                    f.Simulation.ApplyGlobalAttribute((uint)attribute+1,attribute,AttributeOperation.Set,
                        attribute==(outcome==0?GameAttribute.GAMBLE_LOSS_CHANCE:outcome==1?GameAttribute.GAMBLE_RETURN_CHANCE:outcome==2?GameAttribute.GAMBLE_WIN_CHANCE:GameAttribute.GAMBLE_QUADRUPLE_CHANCE)?1:0);
                var round=f.C.Round; round.PendingScore=10; round.Phase=(byte)RoundPhase.Draining; f.C.Round=round;
                CampaignSettlementSystem.Execute(f.C);
                Check(f.C.Round.GamblingMultiplier==outcome,"runtime gambling "+outcome);
                Near(outcome*10,f.C.Round.GamblingPayout,"gambling payout");
            }
            using(var f=new Fixture())
            {
                f.Simulation.ApplyGlobalAttribute(77,GameAttribute.SHOP_PRICE_MOD,AttributeOperation.Set,.5);
                Near(5,CampaignShopSystem.Price(f.C,10),"shop discount");
                f.Add(DeviceKind.Portal);
                var pair=f.Simulation.GetOwnedDevices();
                Check(pair.Length==2 && pair[0].PairId==pair[1].PairId,"purchase a portal pair");
            }
        }
        private static void PhysicalBridge()
        {
            var scene=SceneManager.CreateScene("ContentValidationPhysics",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var contacts=new List<MarbleContact>();
            var bridge=new MarblePhysicsBridge(scene,null,null,contacts.Add);
            try
            {
                var key=new MarbleKey(1,1);
                var settings=new MarblePhysicalSettings{Radius=.08f,Mass=.1f,Bounce=.6f,Friction=.25f,Gravity=0,MaxSpeed=12,MagnetResponse=1};
                Check(bridge.TrySpawn(new MarbleSpawnData{Key=key,Position=new Vector3(0,2,0),Rotation=Quaternion.identity,
                    Quote=new ShotQuote(1,1,.08f,.1f,10,1),LinearVelocity=Vector3.forward,HasRestitution=true,Restitution=.6f,HasSettings=true,Settings=settings}),"real body spawn");
                bridge.Simulate(.1f);bridge.TryGetState(key,out var state);
                Near(2,state.Position.y,"zero gravity");
                settings.Gravity=2;settings.MaxSpeed=2;bridge.SetProperties(key,settings);
                bridge.Simulate(.1f);bridge.TryGetState(key,out state);
                Check(state.Position.y<2 && state.LinearVelocity.magnitude<=2.001f,"gravity and speed cap");
                bridge.Remove(key);
                var pinObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                SceneManager.MoveGameObjectToScene(pinObject,scene);pinObject.transform.position=new Vector3(0,2,.5f);pinObject.transform.localScale=Vector3.one*.2f;
                var pin=pinObject.AddComponent<MarblePin>();pin.TargetId=321;pin.ScoreMultiplier=1;pin.RushChanceAdd=0;
                settings.Gravity=0;settings.PierceCount=1;settings.MaxSpeed=12;
                Check(bridge.TrySpawn(new MarbleSpawnData{Key=new MarbleKey(1,2),Position=new Vector3(0,2,0),Rotation=Quaternion.identity,
                    Quote=new ShotQuote(1,1,.08f,.1f,10,1),LinearVelocity=Vector3.forward*5,HasRestitution=true,Restitution=.6f,HasSettings=true,Settings=settings}),"piercing spawn");
                bridge.Simulate(.2f);
                Check(contacts.Any(x=>x.Kind==MarbleContactKind.Pierced),"piercing before collision");
                bridge.TryGetState(new MarbleKey(1,2),out state);
                Check(state.Position.z>.6f,"passes through pin");
                bridge.Remove(new MarbleKey(1,2));UnityEngine.Object.DestroyImmediate(pinObject);
                var magnetObject=new GameObject("MagnetValidation");SceneManager.MoveGameObjectToScene(magnetObject,scene);
                magnetObject.transform.position=new Vector3(0,2,.15f);
                var magnet=magnetObject.AddComponent<MarbleDevice>();magnet.InstanceId=1;magnet.Kind=DeviceKind.Magnet;magnet.Range=1;magnet.Strength=1;
                settings.PierceCount=0;settings.Magnetic=true;
                Check(bridge.TrySpawn(new MarbleSpawnData{Key=new MarbleKey(1,3),Position=new Vector3(0,2,0),Rotation=Quaternion.identity,
                    Quote=new ShotQuote(1,1,.08f,.1f,10,1),LinearVelocity=Vector3.right*.01f,HasRestitution=true,Restitution=.6f,HasSettings=true,Settings=settings}),"magnetic spawn");
                bridge.Simulate(.02f);bridge.TryGetState(new MarbleKey(1,3),out state);
                Check(state.LinearVelocity.z>0,"lower pole attraction");
                UnityEngine.Object.DestroyImmediate(magnetObject);
            }
            finally
            {
                bridge.Dispose();
                SceneManager.UnloadSceneAsync(scene);
            }
        }
        private static void Boundaries()
        {
            var recipes = Balance().Content;
            var imported = GameContentImporter.Read(GameContentImporter.Workbook, recipes);
            Check(imported.Attributes.Length == 34 && imported.Devices.Length == 15 && imported.Drugs.Length == 12, "real workbook import preserves migrated legacy definitions");
            foreach (var legacy in recipes.Devices.Where(x => x.Kind == DeviceKind.LegacyPin))
                Check(JsonUtility.ToJson(legacy) == JsonUtility.ToJson(imported.Devices.Single(x => x.Id == legacy.Id)), "import preserves legacy device values " + legacy.Id);
            foreach (var legacy in recipes.Drugs.Where(x => x.Kind == DrugKind.LegacyBounce))
                Check(JsonUtility.ToJson(legacy) == JsonUtility.ToJson(imported.Drugs.Single(x => x.Id == legacy.Id)), "import preserves legacy drug values " + legacy.Id);
            Near(.25, imported.Definition(GameAttribute.GAMBLE_QUADRUPLE_CHANCE).DefaultValue, "import confirmed fourth outcome");
            Check(imported.Devices.Count(x => x.SourceId == "D009") == 2, "duplicate authoring IDs do not merge devices");
            using (var f = new Fixture())
            {
                f.C.Tuning.MinInvestment = f.C.Tuning.MaxInvestment = 5;
                f.Simulation.ApplyGlobalAttribute(1, GameAttribute.BLOOD_COST, AttributeOperation.Add, 1);
                Near(6, f.Simulation.QuoteShot(0).Cost, "cost ADD after investment calculation");
            }
            using (var f = new Fixture())
            {
                var device = f.Add(DeviceKind.Lens); var key = f.Launch();
                f.Simulation.ApplyDeviceAttribute(device.InstanceId, 1, GameAttribute.DEVICE_COOLDOWN, AttributeOperation.Set, 2);
                f.Simulation.ApplyDeviceAttribute(device.InstanceId, 2, GameAttribute.MACHINE_MULT, AttributeOperation.Set, 2);
                f.Step(f.Hit(key, device)); f.Step(f.Hit(key, device));
                Near(20, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "device cooldown seconds");
                Near(2, f.Simulation.GetMarbleAttribute(key, GameAttribute.MACHINE_MULT), "device machine multiplier");
                f.Simulation.ApplyAttribute(key, 2, GameAttribute.BALL_LIFETIME, AttributeOperation.Set, 1);
                for (int i = 0; i < 12; i++) f.Step();
                Check(!f.C.Marbles.ContainsKey(key), "lifetime attribute actually expires body");
            }
            using (var f = new Fixture())
            {
                var key = f.Launch();
                f.Simulation.ApplyAttribute(key, 8, GameAttribute.BALL_RUSH_GAIN, AttributeOperation.Set, 10);
                f.Step(new MarbleContact { Key = key, TargetId = 1, Kind = MarbleContactKind.Score, Multiplier = 1 });
                Check(f.C.Round.RushRemaining > 0, "rush contribution activates meter");
                double remaining = f.C.Round.RushRemaining;
                f.Simulation.ApplyGlobalAttribute(9, GameAttribute.RUSH_DECAY, AttributeOperation.Set, 0);
                f.Step(); Near(remaining, f.C.Round.RushRemaining, "zero rush decay");
            }
            using (var f = new Fixture())
            {
                f.Simulation.UseDrug(f.C.Balance.Drugs.Single(x => x.Kind == DrugKind.Replicator).Id);
                var key = f.Launch(); var revive = f.Add(DeviceKind.Revive);
                f.Step(f.Hit(key, revive));
                f.Step(new MarbleContact { Key = key, TargetId = 123, Kind = MarbleContactKind.Pin, Multiplier = 1 });
                var keys = f.C.Marbles.Keys.ToArray();
                var player = f.C.Player; player.Blood = 100; f.C.Player = player;
                f.Step(keys.Select(k => new MarbleContact { Key = k, TargetId = 1, Kind = MarbleContactKind.Drain }).ToArray());
                Near(102, f.C.Player.Blood, "clone family refunds original cost exactly once");
            }
        }
        private sealed class FakePhysics : IMarblePhysics, IMarblePhysicsStateProvider, IMarblePhysicsCommands
        {
            internal readonly Dictionary<MarbleKey,MarbleSpawnData> Spawns=new Dictionary<MarbleKey,MarbleSpawnData>();
            internal readonly Dictionary<MarbleKey,MarblePhysicsState> States=new Dictionary<MarbleKey,MarblePhysicsState>();
            internal readonly Dictionary<int,DevicePose> Poses=new Dictionary<int,DevicePose>();
            internal int FailSpawnAt,Attempts,Moves,CannonShots;
            public bool TrySpawn(MarbleSpawnData data)
            {
                if(++Attempts==FailSpawnAt)return false;
                Spawns.Add(data.Key,data);States.Add(data.Key,new MarblePhysicsState{Position=data.Position,Rotation=data.Rotation,LinearVelocity=data.LinearVelocity});return true;
            }
            public bool TryGetPosition(MarbleKey key,out Vector3 p){bool ok=States.TryGetValue(key,out var s);p=s.Position;return ok;}
            public bool TryGetState(MarbleKey key,out MarblePhysicsState state)=>States.TryGetValue(key,out state);
            public void Remove(MarbleKey key){Spawns.Remove(key);States.Remove(key);}
            public bool TryMove(MarbleKey key,Vector3 p,Vector3 v){var s=States[key];s.Position=p;s.LinearVelocity=v;States[key]=s;Moves++;return true;}
            public void SetProperties(MarbleKey key,MarblePhysicalSettings settings){}
            public bool TryGetDevicePose(int id,out DevicePose pose)=>Poses.TryGetValue(id,out pose);
            public void FireBloodCannon(int id){CannonShots++;}
        }
    }
}
