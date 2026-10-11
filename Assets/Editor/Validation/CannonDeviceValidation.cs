using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static class CannonDeviceValidation
    {
        private static int assertions;
        public static void Validate()
        {
            assertions = 0;
            RotationAndShots();
            CapacityAndCompletion();
            LifetimeAndCleanup();
            HoldApi();
            Debug.Log("CANNON_VALIDATION_PASSED: " + assertions + " assertions; eight headings, five-ball storage, click release, spacing, durability and hold lifecycle.");
        }
        private static void RotationAndShots()
        {
            using (var r = new Rig())
            {
                var initial = r.Pose.Forward;
                for (int i = 0; i <= 8; i++)
                {
                    r.Time(i * .5); r.Tick();
                    Near(Quaternion.AngleAxis((i % 8) * 45, r.Pose.Up) * initial, r.Pose.Forward, "eight discrete headings on an inclined board");
                    Near(r.Pose.Forward, (r.View.Root.transform.Find("Muzzle").position - r.View.Root.transform.TransformPoint(new Vector3(0, .16f, 0))).normalized,
                        "visible muzzle matches physical heading");
                }
                var api = new DeviceEventContext(r.C, r.Device);
                Check(!api.TrySetYaw(float.NaN) && !api.TrySetYaw(float.PositiveInfinity), "invalid angles rejected");
                api.Close(); Expired(() => api.TrySetYaw(0)); Expired(() => api.ReleaseHeld()); Expired(() => { _ = api.HeldCount; });
            }
            for (int heading = 0; heading < 8; heading++)
                using (var r = new Rig())
                {
                    var keys = Enumerable.Range(0, 5).Select(_ => r.Launch()).ToArray();
                    r.Contact(keys);
                    foreach (var key in keys) Check(r.Held(key) && r.Body(key).isKinematic && !r.Body(key).detectCollisions, "stored balls are suspended");
                    Check(r.Uses == 0, "storage costs no durability");
                    r.Time(heading * .5 + 8); r.Tick(); var direction = r.Pose.Forward;
                    // Cross a rotation boundary without another tick: click must use the displayed heading.
                    r.Time(heading * .5 + 8.6);
                    Check(r.Click() && r.HeldCount == 0, "click releases every stored ball");
                    for (int i = 0; i < keys.Length; i++)
                    {
                        var body = r.Body(keys[i]);
                        Check(!body.isKinematic && body.detectCollisions, "release restores physical participation");
                        Near(direction * 6, body.velocity, "all shots use clicked heading and configured speed");
                        Near(new Vector3(1, 2, 3), body.angularVelocity, "release preserves spin");
                        float radius = r.Marble(keys[i]).Radius;
                        Check(Vector3.Dot(body.position - r.Pose.Position, direction) > r.Pose.Radius + radius,
                            "shot starts outside the source");
                        for (int j = 0; j < i; j++)
                            Check(Vector3.Distance(body.position, r.Body(keys[j]).position) >= radius + r.Marble(keys[j]).Radius + .019f,
                                "batch shots never overlap");
                    }
                    Check(r.Uses == 1 && r.Click() && r.Uses == 1, "one volley costs one use and empty clicks cost nothing");
                    r.Contact(keys); Check(r.HeldCount == 0, "emitted balls cannot be recaptured by the same cannon");
                }
        }
        private static void CapacityAndCompletion()
        {
            using (var r = new Rig())
            {
                var keys = Enumerable.Range(0, 6).Select(_ => r.Launch()).ToArray(); r.Contact(keys);
                Check(r.HeldCount == 5 && !r.Held(keys[5]) && r.Uses == 0, "six arrivals store exactly five without spending durability");
                Check(r.Simulation.GetDeviceStatus(r.Id).Contains("5/5"), "Lua publishes capacity status");
                r.Time(r.C.Round.Time + 61); r.Step();
                Check(r.HeldCount == 5, "manual hold has no timed 60-second auto-release");
                Check(r.Click(), "stored batch fires on demand");
                r.Contact(new[] { keys[5] }); Check(r.HeldCount == 1 && r.Uses == 1, "capacity frees after firing and new storage is free");
                var second = r.Add(new Vector3(4, 0, 0));
                var key = r.Launch(); r.Contact(new[] { key }, second.Root.GetComponent<MResDeviceBody>());
                Check(r.HeldCount == 1, "another cannon has independent storage");
                r.Click(); Check(r.Held(key), "one cannon cannot release another cannon's ball");
            }
            using (var r = new Rig())
            {
                r.C.Balance.Devices.Single(d => d.Id == 9007).Durability = 1;
                var keys = Enumerable.Range(0, 5).Select(_ => r.Launch()).ToArray(); r.Contact(keys);
                Check(r.HeldCount == 5 && r.Uses == 0, "last durability point accepts a complete batch for free");
                var ray = new Ray(r.Pose.Position + r.Pose.Up * 2, -r.Pose.Up);
                Check(r.Bridge.TryPickDevice(ray, out int picked) && picked == r.Id, "stored cannon remains mouse-pickable");
                Check(r.Click() && keys.All(k => !r.Held(k)) && r.Uses == 1, "last use releases all five balls");
                var extra = r.Launch(); r.Contact(new[] { extra }); Check(!r.Held(extra), "exhaustion rejects new storage");
                Check(!r.Click() && !r.Bridge.TryPickDevice(ray, out _), "empty exhausted cannon disables further activation");
                Check(r.Simulation.GetDeviceStatus(r.Id) == string.Empty, "empty exhausted cannon has no stale active status");
            }
            using (var r = new Rig())
            {
                var keys = Enumerable.Range(0, 5).Select(_ => r.Launch()).ToArray(); r.Contact(keys);
                var obstacle = new GameObject("BlockedCannonMuzzle"); SceneManager.MoveGameObjectToScene(obstacle, r.Scene);
                obstacle.transform.position = r.Pose.Position + r.Pose.Up * .085f + r.Pose.Forward * .35f;
                obstacle.AddComponent<BoxCollider>().size = Vector3.one * .2f; Physics.SyncTransforms();
                try { Check(r.Click() && r.HeldCount == 5 && r.Uses == 0, "fully blocked volley stays held and costs nothing"); }
                finally { UnityEngine.Object.DestroyImmediate(obstacle); Physics.SyncTransforms(); }
                Check(r.Click() && r.HeldCount == 0 && r.Uses == 1, "successful retry spends exactly one use");
            }
            using (var r = new Rig())
            {
                r.C.Balance.Devices.Single(d => d.Id == 9007).Durability = 1;
                var keys = Enumerable.Range(0, 5).Select(_ => r.Launch()).ToArray(); r.Contact(keys);
                var obstacle = new GameObject("BlockedSecondShot"); SceneManager.MoveGameObjectToScene(obstacle, r.Scene);
                obstacle.transform.position = r.Pose.Position + r.Pose.Up * .085f + r.Pose.Forward * .53f;
                obstacle.AddComponent<SphereCollider>().radius = .01f; Physics.SyncTransforms();
                try { Check(r.Click() && r.HeldCount == 4 && r.Uses == 1, "partial volley spends the final use but retains blocked shots"); }
                finally { UnityEngine.Object.DestroyImmediate(obstacle); Physics.SyncTransforms(); }
                var ray = new Ray(r.Pose.Position + r.Pose.Up * 2, -r.Pose.Up);
                Check(r.Bridge.TryPickDevice(ray, out int picked) && picked == r.Id, "unfinished last volley remains pickable");
                var extra = r.Launch(); r.Contact(new[] { extra }); Check(!r.Held(extra), "new balls cannot join a paid pending volley");
                Check(r.Bridge.TryMove(keys[0], r.Pose.Position + r.Pose.Up * 3, Vector3.zero), "clear the first emitted shot");
                Check(r.Click() && r.HeldCount == 0 && r.Uses == 1, "retry completes the paid volley without spending again");
            }
            using (var r = new Rig())
            {
                r.Simulation.ApplyGlobalAttribute(990, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var key = r.Launch();
                Check(r.Bridge.TryMove(key, r.Board.transform.TransformPoint(new Vector3(0, .08f, .8f)), r.Board.transform.rotation * Vector3.back * 2), "physical trigger setup");
                for (int i = 0; i < 10 && !r.Held(key); i++) r.Step(true);
                Check(r.Held(key), "real cylinder trigger stores the ball through packaged Lua");
                var ray = new Ray(r.Pose.Position + r.Pose.Up * 2, -r.Pose.Up);
                Check(r.Bridge.TryPickDevice(ray, out int picked) && picked == r.Id && r.Simulation.TryActivateDevice(picked), "actual local-scene click fires cannon");
                Near(r.Pose.Forward * 6, r.Body(key).velocity, "physical entry leaves at configured launch speed");
            }
        }
        private static void LifetimeAndCleanup()
        {
            foreach (int mode in new[] { 0, 1, 2 })
                using (var r = new Rig())
                {
                    var key = r.Launch(); r.Contact(new[] { key });
                    if (mode == 0) r.C.Manager.DestroyEntity(r.Device);
                    else if (mode == 1) { var d = r.C.Manager.GetComponentData<OwnedDeviceData>(r.Device); d.Placed = false; r.C.Manager.SetComponentData(r.Device, d); }
                    else r.View.Root.SetActive(false);
                    r.Step(); Check(!r.Held(key) && !r.Body(key).isKinematic, "source removal, recall or missing view releases storage");
                    Near(new Vector3(3, 0, 4), r.Body(key).velocity, "cancellation resumes entry velocity without firing");
                    if (mode != 0) Check(r.Uses == 0, "cancelled storage costs no durability");
                }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Contact(new[] { key }); var body = r.Body(key);
                BallInteractionSystem.Consume(r.C, r.Ball(key), false); DespawnSystem.Execute(r.C, r.Bridge);
                Check(r.HeldCount == 0, "vanished ball frees capacity without stale Lua IDs");
                var next = r.Launch(); Check(r.Body(next) == body && !r.Held(next) && !body.isKinematic && body.detectCollisions, "pooled ball has no manual hold");
                r.Contact(new[] { next });
                var round = r.C.Round; round.Phase = (byte)RoundPhase.Build; r.C.Round = round;
                Check(!r.Click(), "build phase rejects activation");
                r.Simulation.BeginRound(); r.Tick();
                Check(r.C.Marbles.Count == 0 && r.HeldCount == 0 && r.Simulation.GetDeviceStatus(r.Id).Contains("0/5"), "next round resets storage and status");
                next = r.Launch(); r.Contact(new[] { next }); r.Simulation.StartSession(r.Bridge);
                Check(r.C.Marbles.Count == 0 && r.Bridge.ActiveBodyCount == 0, "session reset cleans pending storage");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Contact(new[] { key });
                var f = r.C.Manager.GetComponentData<BallFeatures>(r.Ball(key)); f.BirthTick = r.C.Round.Tick - 1199;
                r.C.Manager.SetComponentData(r.Ball(key), f); r.Step();
                Check(!r.C.Marbles.ContainsKey(key) && r.HeldCount == 0, "normal lifetime still expires inside storage");
            }
        }
        private static void HoldApi()
        {
            using (var r = new Rig(@"
local previous, completed, info
function OnContact(a) previous = a; assert(a.Motion:TryHoldUntilReleased()) end
function OnActivate(a) a:ReleaseHeld() end
function OnHoldEnd(a)
    assert(previous ~= a and not pcall(function() return previous.Time end))
    assert(a.Hold.IsManual and a.Hold.Duration == 0 and a:TryUse())
    assert(not a:Trigger(0))
    completed, info = a, a.Hold
    assert(a.Motion:TrySetVelocity(a.Hold.EntryVelocity * 2))
end
function VerifyClosed()
    assert(not pcall(function() return completed.Time end))
    assert(not pcall(function() return info.IsManual end))
end"))
            {
                var key = r.Launch(); r.Contact(new[] { key }); var position = r.Body(key).position;
                Check(r.Click() && !r.Held(key), "manual release invokes fresh callback");
                Near(position, r.Body(key).position, "hold/release alone do not move ball");
                Near(new Vector3(6, 0, 8), r.Body(key).velocity, "completion independently chooses an unrelated velocity");
                r.Script.Invoke("VerifyClosed");
            }
            using (var r = new Rig("function OnContact(a) assert(a.Motion:TryHoldUntilReleased()) end\nfunction OnActivate(a) a:ReleaseHeld() end"))
            {
                var key = r.Launch(); r.Contact(new[] { key });
                Check(r.Click() && !r.Held(key), "release works without OnHoldEnd"); Near(new Vector3(3, 0, 4), r.Body(key).velocity, "missing handler restores motion");
                var api = r.Api(key); Check(api.Motion.TryHoldUntilReleased() && !api.Motion.TryHoldUntilReleased(), "duplicate manual hold cannot overwrite snapshot");
                api.Close(); Expired(() => api.Motion.TryHoldUntilReleased());
            }
            using (var r = new Rig(@"
local completed = 0
function OnContact(a) assert(a.Motion:TryHold(1)) end
function OnActivate(a) a:ReleaseHeld() end
function OnHoldEnd(a)
    completed = completed + 1
    if completed == 1 then error('EXPECTED_CANNON_COMPLETION_FAILURE') end
    a:Set('BALL_BASE_VALUE', a.Value + 1)
end
function Verify() assert(completed == 2) end"))
            {
                var first = r.Launch(); var second = r.Launch(); r.Contact(new[] { first, second });
                int errors = 0;
                Application.LogCallback listener = (text, trace, kind) => { if (text.Contains("EXPECTED_CANNON_COMPLETION_FAILURE")) errors++; };
                Application.logMessageReceived += listener;
                try { Check(r.Click(), "batch can complete timed holds early"); }
                finally { Application.logMessageReceived -= listener; }
                Check(errors == 1 && !r.Held(first) && !r.Held(second), "one callback failure cannot strand the batch");
                r.Script.Invoke("Verify");
            }
        }
        private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception("Cannon: " + message); }
        private static void Near(Vector3 expected, Vector3 actual, string message) => Check((expected - actual).magnitude < .0002f, message + ": " + expected + " vs " + actual);
        private static void Expired(Action action)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "callback API expires"); }

        private static byte[] WithLua(MResPackage package, string lua)
        {
            var entries = package.Entries.Values.ToArray(); var offsets = new List<long>(); var lengths = new List<int>();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(new byte[28]);
                foreach (var entry in entries)
                { var bytes = entry.Type == MResEntryType.Lua ? Encoding.UTF8.GetBytes(lua) : package.GetBytes(entry.Path);
                    offsets.Add(stream.Position); lengths.Add(bytes.Length); writer.Write(bytes); }
                long toc = stream.Position;
                for (int i = 0; i < entries.Length; i++)
                {
                    var path = Encoding.UTF8.GetBytes(entries[i].Path); writer.Write(path.Length); writer.Write(path);
                    writer.Write((byte)entries[i].Type); writer.Write((byte)0); writer.Write((ushort)0);
                    writer.Write((ulong)offsets[i]); writer.Write((ulong)lengths[i]);
                }
                long size = stream.Position - toc; stream.Position = 0;
                writer.Write(Encoding.ASCII.GetBytes("MRES")); writer.Write((ushort)1); writer.Write((ushort)0);
                writer.Write(entries.Length); writer.Write((ulong)toc); writer.Write((ulong)size); return stream.ToArray();
            }
        }
        private sealed class Rig : IDisposable
        {
            internal readonly Scene Scene;
            internal readonly GameObject Board;
            internal readonly MarbleSimulation Simulation;
            internal readonly MarblePhysicsBridge Bridge;
            internal readonly Entity Device;
            internal readonly MResLoadHandle View;
            private readonly List<MResLoadHandle> views = new List<MResLoadHandle>();
            private readonly DeviceResourceCatalog catalog;
            private readonly DeviceResourceCatalog.Entry[] original;
            private int launches;
            internal SimulationContext C => Simulation.Context;
            internal int Id => View.Root.GetComponent<MResDeviceBody>().InstanceId;
            internal DevicePose Pose { get { Bridge.TryGetDevicePose(Id, out var pose); return pose; } }
            internal MResLuaScript Script => C.DeviceScripts.Get(Id, 9007);
            internal int HeldCount => HeldMotionSystem.Count(C, Id);
            internal int Uses => C.Manager.GetComponentData<OwnedDeviceData>(Device).UsesThisRound;
            internal Rig(string lua = null)
            {
                if (lua != null)
                {
                    catalog = DeviceResourceCatalog.Current; original = catalog.Entries;
                    var bytes = WithLua(catalog.Get(9007).Package, lua);
                    catalog.Entries = original.Select(e => e.File.EndsWith("9007_Cannon.mres", StringComparison.Ordinal) ? new DeviceResourceCatalog.Entry { File = e.File, Bytes = bytes } : e).ToArray();
                    catalog.Invalidate();
                }
                Scene = SceneManager.CreateScene("CannonValidation_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var balance = GameBalanceLoader.Load(); foreach (var d in balance.Devices) d.StartingCount = 0;
                balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 1000;
                balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue = 120;
                Simulation = new MarbleSimulation(balance); Bridge = new MarblePhysicsBridge(Scene, null, null, Simulation.EnqueueContact);
                Simulation.StartSession(Bridge); Simulation.BeginRound();
                Board = new GameObject("CannonBoard"); SceneManager.MoveGameObjectToScene(Board, Scene);
                Board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 37, 0));
                View = Add(Vector3.zero); CampaignStateUtility.FindDevice(C, Id, out Device); Tick();
            }
            internal MResLoadHandle Add(Vector3 offset)
            {
                var definition = C.Balance.Devices.Single(d => d.Id == 9007); CampaignStateUtility.CreateDevice(C, definition, definition.Price);
                var owned = Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last(); CampaignStateUtility.FindDevice(C, owned.InstanceId, out var entity);
                var data = C.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; C.Manager.SetComponentData(entity, data);
                var view = MResDeviceView.Create(owned, Board.transform, C.DeviceScripts.Get(owned.InstanceId, definition.Id));
                view.Root.transform.localPosition = offset; views.Add(view); view.Root.SetActive(true); Physics.SyncTransforms(); return view;
            }
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            internal MarbleBody Marble(MarbleKey key) => Scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key));
            internal Rigidbody Body(MarbleKey key) => Marble(key).Body;
            internal bool Held(MarbleKey key) => C.Manager.HasComponent<HeldMotion>(Ball(key));
            internal DeviceContactContext Api(MarbleKey key) => new DeviceContactContext(C, Ball(key), Device, View.Root.GetComponent<MResDeviceBody>().Contact(key, Body(key).position), Pose);
            internal MarbleKey Launch()
            {
                Simulation.FireOnce(); Simulation.BeforePhysics(.1f, Bridge, new Vector3(3 + launches++ * .4f, 5, 0), Quaternion.identity); Simulation.AfterPhysics(Bridge);
                var key = C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Body(key).drag = Body(key).angularDrag = 0; return key;
            }
            internal void Contact(IEnumerable<MarbleKey> keys, MResDeviceBody source = null)
            {
                Simulation.BeforePhysics(.1f, Bridge, Vector3.up * 10, Quaternion.identity);
                foreach (var key in keys)
                { Bridge.TrySetVelocity(key, new Vector3(3, 0, 4)); Body(key).angularVelocity = new Vector3(1, 2, 3);
                    Simulation.EnqueueContact((source ?? View.Root.GetComponent<MResDeviceBody>()).Contact(key, Body(key).position)); }
                Simulation.AfterPhysics(Bridge);
            }
            internal bool Click() => Simulation.TryActivateDevice(Id);
            internal void Time(double time) { var round = C.Round; round.Time = time; C.Round = round; }
            internal void Tick() => DeviceScriptSystem.DeviceEvent(C, Id, "OnTick");
            internal void Step(bool physics = false)
            { Simulation.BeforePhysics(.1f, Bridge, Vector3.up * 10, Quaternion.identity); if (physics) Bridge.Simulate(.1f); Simulation.AfterPhysics(Bridge); }
            public void Dispose()
            {
                try { foreach (var view in views) view.Dispose(); Simulation.Dispose(); Bridge.Dispose(); UnityEngine.Object.DestroyImmediate(Board); SceneManager.UnloadSceneAsync(Scene); }
                finally { if (catalog != null) { catalog.Entries = original; catalog.Invalidate(); } }
            }
        }
    }
}
