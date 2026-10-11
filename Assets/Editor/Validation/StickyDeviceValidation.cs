using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections.Generic;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using Unity.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    [InitializeOnLoad]
    public static class StickyDeviceValidation
    {
        private static int assertions;
        static StickyDeviceValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("StickyValidation.Pending", false)) return;
                SessionState.SetBool("StickyValidation.Pending", false);
                EditorApplication.delayCall += Run;
            };
        }

        [MenuItem("Marbles ECS/Validate Sticky Device")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                if (Application.isBatchMode) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionState.SetBool("StickyValidation.Pending", true); EditorApplication.EnterPlaymode(); return;
            }
            try { Validate(); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }

        public static void Validate()
        {
            assertions = 0;
            using (var r = new Rig())
            {
                r.C.Balance.Devices.Single(d => d.Id == 9001).Durability = 1;
                var key = r.Launch(); r.Contact(key, 5);
                Check(r.Held(key) && r.Simulation.GetOwnedDevices().Single().Exhausted, "final durability use still starts a hold");
                Check(r.View.Root.GetComponentsInChildren<Collider>().All(c => !c.enabled), "exhausted device loses its collision shape");
                for (int i = 0; i < 10; i++) r.Step();
                Check(!r.Held(key) && !r.Body(key).isKinematic, "held completion restores a ball after source durability is depleted");
                VectorNear(r.Down * 5, r.Body(key).velocity, "final held effect still completes its Lua launch");
                Check(r.Simulation.GetOwnedDevices().Single().RemainingDurability == 0, "completion does not charge again");
            }
            foreach (float speed in new[] { 0f, .03f, 5f })
                using (var r = new Rig())
                {
                    var key = r.Launch(); var body = r.Body(key);
                    var position = body.position; var rotation = body.rotation;
                    r.Contact(key, speed);
                    Check(r.Held(key), "packaged Lua starts the hold");
                    Check(body.isKinematic && !body.detectCollisions, "held body cannot be pushed by other balls");
                    VectorNear(Vector3.zero, r.C.Manager.GetComponentData<Motion3D>(r.Ball(key)).LinearVelocity, "ECS velocity is zero immediately");
                    for (int i = 0; i < 9; i++)
                    {
                        r.Step(); Check(r.Held(key), "no early release at " + (i + 1) * .1f);
                        VectorNear(position, body.position, "gravity cannot move the held ball");
                        Check(Quaternion.Angle(rotation, body.rotation) < .001f, "held rotation remains fixed");
                    }
                    // A stale drain contact must not settle a suspended ball.
                    r.Simulation.EnqueueContact(new MarbleContact { Key = key, Kind = MarbleContactKind.Drain, TargetId = 99 });
                    ContactResolutionSystem.Execute(r.C); r.C.Contacts.Clear();
                    Check(r.C.TryGetFlying(key, out _), "held ball ignores stale terminal contacts");
                    r.Step();
                    Check(!r.Held(key) && !body.isKinematic && body.detectCollisions, "release restores dynamic collision mode at one second");
                    Check(body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic, "CCD mode restored");
                    var expected = r.Down * speed;
                    VectorNear(expected, body.velocity, "preserve incoming speed and release downhill on rotated board");
                    VectorNear(expected, r.C.Manager.GetComponentData<Motion3D>(r.Ball(key)).LinearVelocity, "release synchronizes ECS velocity");
                    VectorNear(Rig.Spin, body.angularVelocity, "release restores angular velocity");
                    VectorNear(body.position, r.C.Manager.GetComponentData<Transform3D>(r.Ball(key)).Position, "release synchronizes ECS position");
                    Check(Vector3.Distance(position, body.position) > 1, "clear exit relocation occurred");
                    r.Contact(key, speed);
                    Check(!r.Held(key), "same ball cannot become stuck in repeated holds at the same device");
                }
            using (var r = new Rig())
            {
                var key = r.Launch(); var ball = r.Ball(key);
                var api = r.Api(key);
                var m = api.Motion;
                Check(api.Hold == null, "contact has no completion snapshot");
                Check(!m.TryHold(0) && !m.TryHold(-1) && !m.TryHold(float.NaN) &&
                    !m.TryHold(float.PositiveInfinity) && !m.TryHold(61), "invalid duration has no effects");
                Check(m.TrySetVelocity(Vector3.right * 5) && m.TryHold(1), "hold needs no destination or launch parameters");
                Check(!m.TryHold(1) && !m.TrySetVelocity(Vector3.right), "pending hold is not overwritten");
                api.Close();
                bool expired = false; try { m.TryHold(1); } catch (InvalidOperationException) { expired = true; }
                Check(expired, "API expires at callback boundary");
                Check(r.Simulation.ApplyAttribute(key, 932, GameAttribute.BALL_MAX_SPEED, AttributeOperation.Set, 3), "change held ball's speed cap");
                for (int i = 0; i < 10; i++) r.Step();
                VectorNear(r.Down * 3, r.Body(key).velocity, "release uses the current speed cap");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); var position = r.Body(key).position;
                r.Contact(key, 5);
                var obstacle = new GameObject("BlockedStickyExit"); SceneManager.MoveGameObjectToScene(obstacle, r.Scene);
                float radius = r.Body(key).GetComponent<MarbleBody>().Radius;
                obstacle.transform.position = r.Pose.Position + r.Pose.Up * (radius + .005f) + r.Down * (r.Pose.Radius + radius + .02f);
                obstacle.AddComponent<BoxCollider>().size = Vector3.one * .3f; Physics.SyncTransforms();
                try
                {
                    for (int i = 0; i < 10; i++) r.Step();
                    Check(!r.Held(key), "blocked exit cannot freeze indefinitely");
                    VectorNear(position, r.Body(key).position, "blocked exit keeps the original position");
                    VectorNear(r.Down * 5, r.Body(key).velocity, "blocked exit releases in requested direction");
                }
                finally { UnityEngine.Object.DestroyImmediate(obstacle); }
            }
            foreach (bool remove in new[] { false, true })
                using (var r = new Rig())
                {
                    var key = r.Launch(); r.Contact(key, 5);
                    if (remove) r.C.Manager.DestroyEntity(r.Device);
                    else { var d = r.C.Manager.GetComponentData<OwnedDeviceData>(r.Device); d.Placed = false; r.C.Manager.SetComponentData(r.Device, d); }
                    r.Step();
                    Check(!r.Held(key), "removed or recalled device cancels pending hold");
                    VectorNear(new Vector3(3, 0, 4), r.Body(key).velocity, "cancel restores original entry direction and speed");
                    VectorNear(Rig.Spin, r.Body(key).angularVelocity, "cancel restores spin");
                }
            using (var r = new Rig())
            {
                var first = r.Launch(); r.Contact(first, 5);
                var second = r.Launch(); r.Contact(second, 2);
                Check(r.Held(first) && r.Held(second), "two balls hold independently");
                for (int i = 0; i < 8; i++) r.Step();
                Check(!r.Held(first) && r.Held(second), "first ball releases without releasing second");
                r.Step(); r.Step(); Check(!r.Held(second), "second ball completes its own full delay");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Contact(key, 5);
                var body = r.Body(key);
                var features = r.C.Manager.GetComponentData<BallFeatures>(r.Ball(key));
                features.BirthTick = r.C.Round.Tick - 599; r.C.Manager.SetComponentData(r.Ball(key), features); r.Step();
                Check(!r.C.Marbles.ContainsKey(key), "normal lifespan still expires during hold");
                var next = r.Launch();
                Check(r.Body(next) == body && !body.isKinematic && body.detectCollisions && !r.Held(next), "pooled ball has no suspended state or timer");
                r.Contact(next, 2); r.Simulation.StartSession(r.Bridge);
                Check(r.Bridge.ActiveBodyCount == 0 && r.C.Marbles.Count == 0, "session reset discards held balls and timers");
            }
            using (var r = new Rig())
            {
                // Real convex-cylinder trigger and swept sensor path, without synthesizing a contact.
                r.Simulation.ApplyGlobalAttribute(934, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var key = r.Launch(r.Board.transform.TransformPoint(new Vector3(0, .08f, .8f)));
                r.Bridge.TrySetVelocity(key, r.Board.transform.rotation * Vector3.back * 2);
                int steps = 0;
                while (!r.Held(key) && steps++ < 8) r.Step();
                Check(r.Held(key), "packaged cylinder physically triggers Lua");
                for (int i = 0; i < 10; i++) r.Step();
                Check(!r.Held(key), "physical trigger releases after delay");
                VectorNear(r.Down * 2, r.Body(key).velocity, "real entry speed is preserved");
            }
            HoldComposition();
            Debug.Log("STICKY_VALIDATION_PASSED: " + assertions + " assertions; packaged Lua, PhysX cylinder, independent hold/launch, callbacks and cleanup.");
        }

        private static void HoldComposition()
        {
            using (var r = new Rig("function OnContact(a) a.Motion:TryHold(0.2) end"))
            {
                var key = r.Launch(); var position = r.Body(key).position; r.Contact(key, 5);
                r.Step(); r.Step();
                Check(!r.Held(key), "hold completes without any OnHoldEnd function");
                VectorNear(position, r.Body(key).position, "hold alone never launches or teleports");
                VectorNear(new Vector3(3, 0, 4), r.Body(key).velocity, "hold alone resumes entry velocity");
                VectorNear(Rig.Spin, r.Body(key).angularVelocity, "hold alone resumes spin");
            }
            using (var r = new Rig(@"
local old, oldEndpoint, completed, info
local count = 0
function OnContact(a)
    old = a; oldEndpoint = a.Motion.Source
    assert(a.Motion:TryHold(0.2))
end
function OnHoldEnd(a)
    count = count + 1
    assert(old ~= a and a.Hold ~= nil)
    assert(not pcall(function() return old.Time end))
    assert(not pcall(function() return oldEndpoint.Forward end))
    assert(not a.Motion:TryLaunch(oldEndpoint, a.Motion.Source.Down, 5))
    assert(not a:Trigger(0))
    assert(math.abs(a.Hold.Duration - 0.2) < 0.0001 and a.Hold.Elapsed >= 0.2)
    assert(math.abs(a.Hold.EntrySpeed - 5) < 0.0001)
    assert(math.abs(a.Hold.EntryAngularVelocity.y - 2) < 0.0001)
    completed = a; info = a.Hold
    a:Set('BALL_BASE_VALUE', a.Value * 2)
    assert(a.Motion:TrySetVelocity(a.Hold.EntryVelocity * 2))
end
function VerifyClosed()
    assert(count == 1)
    assert(not pcall(function() return completed.Motion.Speed end))
    assert(not pcall(function() return completed.Hold end))
    assert(not pcall(function() return info.EntrySpeed end))
end"))
            {
                var key = r.Launch(); double value = r.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE);
                var position = r.Body(key).position; r.Contact(key, 5); r.Step(); r.Step();
                Check(r.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE) == value * 2, "completion composes an unrelated attribute effect");
                VectorNear(new Vector3(6, 0, 8), r.Body(key).velocity, "completion independently sets velocity");
                VectorNear(position, r.Body(key).position, "completion need not use an exit");
                r.Script.Invoke("VerifyClosed"); r.Step(); r.Script.Invoke("VerifyClosed");
                Check(true, "fresh context and hold snapshot expire; completion fires once");
            }
            using (var r = new Rig(@"
local count = 0
function OnContact(a) assert(a.Motion:TryHold(0.1)) end
function OnHoldEnd(a)
    count = count + 1
    if count == 1 then assert(a.Motion:TryHold(0.1))
    else a:Set('BALL_BASE_VALUE', a.Value + 1) end
end
function Verify() assert(count == 2) end"))
            {
                var key = r.Launch(); r.Contact(key, 5); r.Step();
                Check(r.Held(key), "completion can start a new independent hold");
                r.Step(); Check(!r.Held(key), "second hold completes on a later step");
                VectorNear(new Vector3(3, 0, 4), r.Body(key).velocity, "chained hold captures restored entry speed");
                r.Script.Invoke("Verify");
            }
            using (var r = new Rig(@"
local count = 0
function OnContact(a) a.Motion:TryHold(0.1) end
function OnHoldEnd(a) count = count + 1; a:Consume(false) end
function Verify() assert(count == 1) end"))
            {
                var key = r.Launch(); r.Contact(key, 5); r.Step();
                Check(!r.C.Marbles.ContainsKey(key) && r.Bridge.ActiveBodyCount == 0, "completion can consume the ball");
                r.Script.Invoke("Verify");
            }
            using (var r = new Rig(@"
local count = 0
function OnContact(a) a.Motion:TryHold(0.1) end
function OnHoldEnd(a) count = count + 1 end
function Verify() assert(count == 0) end"))
            {
                var key = r.Launch(); r.Contact(key, 5);
                var features = r.C.Manager.GetComponentData<BallFeatures>(r.Ball(key));
                features.BirthTick = r.C.Round.Tick - 599; r.C.Manager.SetComponentData(r.Ball(key), features);
                r.Step(); Check(!r.C.Marbles.ContainsKey(key), "expiry on the completion step suppresses the callback");
                r.Script.Invoke("Verify");
            }
            using (var r = new Rig())
            {
                var key = r.Launch(); r.Contact(key, 5);
                r.Board.transform.SetPositionAndRotation(new Vector3(-2, 3, 1), Quaternion.Euler(40, 121, 0));
                Physics.SyncTransforms();
                for (int i = 0; i < 10; i++) r.Step();
                VectorNear(r.Down * 5, r.Body(key).velocity, "completion resolves the current device pose");
            }
            using (var r = new Rig(@"
local count = 0
function OnContact(a) end
function OnHoldEnd(a)
    count = count + 1
    if count == 1 then error('EXPECTED_HOLD_COMPLETION_FAILURE') end
    a:Set('BALL_BASE_VALUE', a.Value + 1)
end
function Verify() assert(count == 2) end"))
            {
                var first = r.Launch(); var api = r.Api(first); api.Motion.TryHold(.3f); api.Close();
                var second = r.Launch(); api = r.Api(second); api.Motion.TryHold(.2f); api.Close();
                double before = r.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE) + r.Simulation.GetMarbleAttribute(second, GameAttribute.BALL_BASE_VALUE);
                int errors = 0;
                Application.LogCallback listener = (message, stack, type) =>
                { if (type == LogType.Exception && (message + stack).Contains("EXPECTED_HOLD_COMPLETION_FAILURE")) errors++; };
                Application.logMessageReceived += listener;
                try { r.Step(); r.Step(); }
                finally { Application.logMessageReceived -= listener; }
                Check(errors == 1, "one intentional Lua error is logged; actual=" + errors);
                Check(!r.Held(first) && !r.Held(second), "callback failure cannot leave either ball frozen");
                Check(!r.Body(first).isKinematic && !r.Body(second).isKinematic, "both bodies resume despite one Lua error");
                double after = r.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE) + r.Simulation.GetMarbleAttribute(second, GameAttribute.BALL_BASE_VALUE);
                Check(after == before + 1, "callback failure does not prevent another ball's completion");
                r.Script.Invoke("Verify");
            }
        }

        // In-memory test packages retain the real model/configuration and replace only their Lua source.
        private static byte[] WithLua(MResPackage package, string lua)
        {
            var entries = package.Entries.Values.ToArray();
            var offsets = new List<long>(); var lengths = new List<int>();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(new byte[28]);
                foreach (var entry in entries)
                {
                    byte[] bytes = entry.Type == MResEntryType.Lua ? Encoding.UTF8.GetBytes(lua) : package.GetBytes(entry.Path);
                    offsets.Add(stream.Position); lengths.Add(bytes.Length); writer.Write(bytes);
                }
                long toc = stream.Position;
                for (int i = 0; i < entries.Length; i++)
                {
                    byte[] path = Encoding.UTF8.GetBytes(entries[i].Path);
                    writer.Write(path.Length); writer.Write(path); writer.Write((byte)entries[i].Type);
                    writer.Write((byte)0); writer.Write((ushort)0); writer.Write((ulong)offsets[i]); writer.Write((ulong)lengths[i]);
                }
                long size = stream.Position - toc; stream.Position = 0;
                writer.Write(Encoding.ASCII.GetBytes("MRES")); writer.Write((ushort)1); writer.Write((ushort)0);
                writer.Write(entries.Length); writer.Write((ulong)toc); writer.Write((ulong)size);
                return stream.ToArray();
            }
        }

        private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception("Sticky: " + message); }
        private static void VectorNear(Vector3 expected, Vector3 actual, string message) => Check((expected - actual).magnitude < .0001f, message + ": " + expected + " vs " + actual);

        private sealed class Rig : IDisposable
        {
            internal static readonly Vector3 Spin = new Vector3(1, 2, 3);
            internal readonly Scene Scene;
            internal readonly GameObject Board;
            internal readonly MarbleSimulation Simulation;
            internal readonly MarblePhysicsBridge Bridge;
            internal readonly Entity Device;
            internal readonly MResLoadHandle View;
            internal readonly OwnedDeviceSnapshot Owned;
            internal SimulationContext C => Simulation.Context;
            internal MResLuaScript Script => C.DeviceScripts.Get(Owned.InstanceId, Owned.DefinitionId);
            internal DevicePose Pose { get { Bridge.TryGetDevicePose(Owned.InstanceId, out var pose); return pose; } }
            internal Vector3 Down => Vector3.ProjectOnPlane(Vector3.down, Pose.Up).normalized;
            private int launches;
            private readonly DeviceResourceCatalog catalog;
            private readonly DeviceResourceCatalog.Entry[] originalEntries;
            internal Rig(string lua = null)
            {
                if (lua != null)
                {
                    catalog = DeviceResourceCatalog.Current; originalEntries = catalog.Entries;
                    var replacement = WithLua(catalog.Get(9001).Package, lua);
                    catalog.Entries = originalEntries.Select(entry => entry.File.EndsWith("9001_Sticky.mres", StringComparison.Ordinal)
                        ? new DeviceResourceCatalog.Entry { File = entry.File, Bytes = replacement } : entry).ToArray();
                    catalog.Invalidate();
                }
                Scene = SceneManager.CreateScene("StickyValidation_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var balance = GameBalanceLoader.Load();
                foreach (var d in balance.Devices) d.StartingCount = 0;
                balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 1000;
                balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue = 60;
                Simulation = new MarbleSimulation(balance);
                Bridge = new MarblePhysicsBridge(Scene, null, null, Simulation.EnqueueContact);
                Simulation.StartSession(Bridge); Simulation.BeginRound();
                var definition = C.Balance.Devices.Single(d => d.Id == 9001);
                CampaignStateUtility.CreateDevice(C, definition, definition.Price);
                Owned = Simulation.GetOwnedDevices().Single();
                CampaignStateUtility.FindDevice(C, Owned.InstanceId, out Device);
                var data = C.Manager.GetComponentData<OwnedDeviceData>(Device); data.Placed = true; C.Manager.SetComponentData(Device, data);
                Board = new GameObject("StickyBoard"); SceneManager.MoveGameObjectToScene(Board, Scene);
                Board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 47, 0));
                View = MResDeviceView.Create(Owned, Board.transform, C.DeviceScripts.Get(Owned.InstanceId, definition.Id));
                View.Root.SetActive(true); Physics.SyncTransforms();
                Check(View.Root.GetComponentInChildren<MeshCollider>().isTrigger, "package contains a cylinder trigger");
            }
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            internal bool Held(MarbleKey key) => C.Marbles.TryGetValue(key, out var entity) && C.Manager.HasComponent<HeldMotion>(entity);
            internal Rigidbody Body(MarbleKey key) => Scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
            internal MarbleKey Launch(Vector3? position = null)
            {
                Simulation.FireOnce();
                Simulation.BeforePhysics(.1f, Bridge, position ?? new Vector3(3 + launches++ * .6f, 4, 0), Quaternion.identity);
                Simulation.AfterPhysics(Bridge);
                var key = C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                Body(key).drag = Body(key).angularDrag = 0;
                return key;
            }
            internal DeviceContactContext Api(MarbleKey key) => new DeviceContactContext(C, Ball(key), Device,
                View.Root.GetComponent<MResDeviceBody>().Contact(key, Body(key).position), Pose);
            internal void Contact(MarbleKey key, float speed)
            {
                Simulation.BeforePhysics(.1f, Bridge, Vector3.up * 10, Quaternion.identity);
                Bridge.TrySetVelocity(key, new Vector3(3, 0, 4).normalized * speed); Body(key).angularVelocity = Spin;
                Simulation.EnqueueContact(View.Root.GetComponent<MResDeviceBody>().Contact(key, Body(key).position));
                Simulation.AfterPhysics(Bridge);
            }
            internal void Step()
            {
                Simulation.BeforePhysics(.1f, Bridge, Vector3.up * 10, Quaternion.identity);
                Bridge.Simulate(.1f); Simulation.AfterPhysics(Bridge);
            }
            public void Dispose()
            {
                try
                {
                    View.Dispose(); Simulation.Dispose(); Bridge.Dispose();
                    UnityEngine.Object.DestroyImmediate(Board); SceneManager.UnloadSceneAsync(Scene);
                }
                finally { if (catalog != null) { catalog.Entries = originalEntries; catalog.Invalidate(); } }
            }
        }
    }
}
