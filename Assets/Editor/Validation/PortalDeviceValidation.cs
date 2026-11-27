using System;
using System.Collections.Generic;
using System.Linq;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static class PortalDeviceValidation
    {
        private static int assertions;
        public static void Validate()
        {
            assertions = 0;
            PairedInstallations();
            SellPair();
            DestroyPairFromLua();
            PreserveMotion();
            PhysicalCrossing();
            for (int fields = 0; fields < 4; fields++) ForceEnvironment((fields & 1) != 0, (fields & 2) != 0);
            Debug.Log("PORTAL_VALIDATION_PASSED: " + assertions + " assertions; paired installations, world motion, physical bidirectional travel and local force recalculation.");
        }

        private static void PairedInstallations()
        {
            using (var r = new Rig(2))
            {
                var owned = r.Owned;
                Check(owned.Length == 4 && owned.All(x => x.DefinitionId == 103), "two purchases give four portal devices");
                Check(owned[0].PairId > 0 && owned[0].PairId == owned[1].PairId && owned[2].PairId == owned[3].PairId &&
                    owned[0].PairId != owned[2].PairId, "each purchase has its own nonzero pair ID");
                Near(2 * r.C.Balance.Devices.Single(d => d.Id == 103).InstallPrice,
                    owned.Sum(device => device.PurchasePrice), "each pair records one installation price");
                Check(owned.All(x => x.Placed), "the two members of each purchase can be placed individually");
                var key = r.Launch(new Vector3(0, .085f, .5f), Vector3.zero);
                for (int i = 0; i < owned.Length; i++)
                {
                    var api = r.Api(key, i);
                    Check(api.Motion.FindPartner().InstanceId == owned[i ^ 1].InstanceId, "partner lookup never crosses purchase groups"); api.Close();
                }
            }
        }

        private static void SellPair()
        {
            using var r = new Rig(2);
            var round = r.C.Round;
            round.Phase = (byte)RoundPhase.Build;
            r.C.Round = round;
            var source = r.Owned[0];
            var partner = r.Owned[1];
            long refund = MarbleRules.RoundScore((source.PurchasePrice + partner.PurchasePrice) * r.C.Balance.Campaign.SellRatio,
                CampaignWalletSystem.MaxBalance);
            Check(source.SellPrice == refund && partner.SellPrice == refund, "both members quote one refund for the full pair");
            Check(r.Simulation.RemoveDevicePlacement(source.InstanceId) && r.Simulation.GetOwnedDevices().Length == 4,
                "returning a portal to inventory keeps all placements and pair membership independently editable");
            int revision = r.C.Campaign.LayoutRevision;
            long scrap = r.C.Player.BiomassScrapBalance;
            var scripts = r.Views.Take(2).Select(view => view.Root.GetComponent<DeviceBody>().Script).ToArray();
            Check(r.Simulation.SellDevice(source.InstanceId), "sell a stored portal whose partner is on the board");
            Check(r.Simulation.GetOwnedDevices().Select(device => device.InstanceId).SequenceEqual(r.Owned.Skip(2).Select(device => device.InstanceId)),
                "selling removes the whole pair and preserves a different pair of the same definition");
            Check(r.C.Player.BiomassScrapBalance == scrap + refund && r.C.Campaign.LayoutRevision == revision + 1,
                "selling the pair credits one refund and changes layout once");
            Check(scripts.All(script => script.IsDisposed) && r.Views.Take(2).All(view => !view.Root.GetComponent<DeviceBody>().GameplayEnabled),
                "both scripts are disposed and both colliders are unavailable after the sale");
            Check(!r.Simulation.SellDevice(partner.InstanceId) && r.C.Player.BiomassScrapBalance == scrap + refund,
                "the deleted partner cannot pay out again");
        }

        private static void DestroyPairFromLua()
        {
            using var r = new Rig(2);
            const string source = @"
local saved, finished
function OnContact(api)
    saved = api
    api:ForEachPlacedDevice(api.DefinitionId, 'RemoveFromField', 0)
    finished = true
end
function RemoveFromField(api)
    saved = api
    api:ForEachBallInRadius(api.Position, 10, 'DestroyPair')
    finished = true
end
function DestroyPair(api)
    api:RemoveDevice()
    api:RemoveDevice()
end
function OnDestroy()
    assert(finished and not pcall(function() return saved.RoundId end), 'Portal teardown preceded callback exit')
end";
            r.C.DeviceScripts.CreateScript = resource => resource.CreateScript(source);
            var scripts = new DeviceLuaScript[2];
            for (int i = 0; i < scripts.Length; i++)
            {
                int id = r.Owned[i].InstanceId;
                r.C.DeviceScripts.Remove(id);
                scripts[i] = r.C.DeviceScripts.Get(id, r.Owned[i].DefinitionId);
            }

            long scrap = r.C.Player.BiomassScrapBalance;
            int revision = r.C.Campaign.LayoutRevision;
            var key = r.Launch(new Vector3(0, .085f, .5f), Vector3.zero);
            bool prematureTeardown = false;
            void ObserveLog(string message, string stack, LogType type)
            {
                if (type == LogType.Exception && message.Contains("Portal teardown preceded callback exit")) prematureTeardown = true;
            }
            Application.logMessageReceived += ObserveLog;
            try { r.Contact(key, 1); }
            finally { Application.logMessageReceived -= ObserveLog; }
            Check(!prematureTeardown && scripts.All(script => script.IsDisposed), "both scripts are disposed after nested Lua callbacks close");
            Check(r.Simulation.GetOwnedDevices().All(device => device.PairId == r.Owned[2].PairId) &&
                r.Simulation.GetOwnedDevices().Length == 2, "Lua destruction removes exactly the source pair");
            Check(r.C.Player.BiomassScrapBalance == scrap && r.C.Campaign.LayoutRevision == revision + 1,
                "repeated Lua removal changes layout once without crediting sale scrap");
        }

        private static void PreserveMotion()
        {
            foreach (float speed in new[] { 0f, .000005f, .03f, 5f })
                using (var r = new Rig())
                {
                    var key = r.Launch(new Vector3(0, .085f, .5f), Vector3.zero);
                    var velocity = new Vector3(3, -4, 1).normalized * speed;
                    Check(r.Bridge.TrySetVelocity(key, velocity), "set arbitrary three-dimensional entry velocity");
                    var body = r.Body(key); body.angularVelocity = new Vector3(2, 3, 4);
                    body.rotation = Quaternion.Euler(21, 42, 63); var rotation = body.rotation;
                    r.Simulation.ApplyAttribute(key, 975, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, .65);
                    r.Simulation.ApplyAttribute(key, 976, GameAttribute.BALL_MASS, AttributeOperation.Set, 2);
                    r.Contact(key, 0);
                    Check((body.position - r.Views[1].Root.transform.position).magnitude < .5f, "portal relocates outside its partner");
                    VectorNear(velocity, body.velocity, "teleport retains the full velocity vector including zero and very low speeds");
                    VectorNear(new Vector3(2, 3, 4), body.angularVelocity, "teleport retains angular velocity");
                    Check(Quaternion.Angle(rotation, body.rotation) < .001f, "teleport retains world rotation");
                    Near(.65, r.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_GRAVITY_MOD), "gravity attribute unchanged");
                    Near(2, r.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_MASS), "mass unchanged");
                    VectorNear(body.position, r.C.Manager.GetComponentData<Transform3D>(r.Ball(key)).Position, "ECS position matches PhysX");
                    VectorNear(velocity, r.C.Manager.GetComponentData<Motion3D>(r.Ball(key)).LinearVelocity, "ECS velocity matches PhysX");
                    var position = body.position; r.Contact(key, 1);
                    VectorNear(position, body.position, "transport lock prevents immediate return through partner");
                }
        }

        private static void PhysicalCrossing()
        {
            using (var r = new Rig())
            {
                var key = r.Launch(new Vector3(0, .085f, .45f), Vector3.back);
                bool moved = false;
                for (int i = 0; i < 30; i++)
                {
                    var before = r.Step(key);
                    if ((r.Body(key).position - before.Position).magnitude < 1) continue;
                    moved = true; VectorNear(before.LinearVelocity, r.Body(key).velocity, "real cylinder entry preserves velocity at relocation"); break;
                }
                Check(moved, "actual packaged cylinder trigger sends ball to the paired portal");
                for (int i = 0; i < 26; i++) r.Step(key);
                var exitTransform = r.Views[1].Root.transform;
                var returningVelocity = exitTransform.rotation * Vector3.back;
                Check(r.Bridge.TryMove(key, exitTransform.TransformPoint(new Vector3(0, .085f, .45f)), returningVelocity), "approach the other side of the pair");
                moved = false;
                for (int i = 0; i < 30; i++)
                {
                    var before = r.Step(key);
                    if ((r.Body(key).position - before.Position).magnitude < 1) continue;
                    moved = true; VectorNear(returningVelocity, r.Body(key).velocity, "reverse transit also keeps world direction"); break;
                }
                Check(moved && (r.Body(key).position - r.Views[0].Root.transform.position).magnitude < .5f, "both purchased devices serve as entry and exit");
            }
        }

        private static void ForceEnvironment(bool entryField, bool exitField)
        {
            using (var r = new Rig())
            {
                Vector3 forceIn = Vector3.zero, forceOut = Vector3.zero;
                if (entryField) forceIn = r.AddField(0, 0);
                if (exitField) forceOut = r.AddField(2, 90);
                var key = r.Launch(new Vector3(0, .085f, 0), Vector3.back * .5f);
                r.Simulation.ApplyAttribute(key, 975, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, .65);
                r.Simulation.ApplyAttribute(key, 976, GameAttribute.BALL_MASS, AttributeOperation.Set, 2);
                var initial = r.Body(key).velocity;
                var beforeTeleport = r.Step(key);
                VectorNear(initial + (Physics.gravity * .65f + forceIn / 2) * Rig.Dt, beforeTeleport.LinearVelocity, "entry gravity and entry field integrate before transit");
                Check((r.Body(key).position - beforeTeleport.Position).magnitude > 1, "force test travels through the real trigger");
                VectorNear(beforeTeleport.LinearVelocity, r.Body(key).velocity, "teleport itself adds no acceleration or impulse");
                var afterTeleport = r.Body(key).velocity;
                r.Step(key);
                VectorNear(afterTeleport + (Physics.gravity * .65f + forceOut / 2) * Rig.Dt, r.Body(key).velocity,
                    "next step keeps personal gravity and recalculates fields at exit (entry=" + entryField + ", exit=" + exitField + ")");
            }
        }

        private static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception("Portal: " + message); }
        private static void Near(double expected, double actual, string message) => Check(Math.Abs(expected - actual) < .0001, message + ": " + expected + " vs " + actual);
        private static void VectorNear(Vector3 expected, Vector3 actual, string message) => Check((expected - actual).magnitude < .0002f, message + ": " + expected + " vs " + actual);

        private sealed class Rig : IDisposable
        {
            internal const float Dt = .02f;
            internal readonly Scene Scene;
            internal readonly GameObject Board;
            internal readonly MarbleSimulation Simulation;
            internal readonly MarblePhysicsBridge Bridge;
            internal readonly OwnedDeviceSnapshot[] Owned;
            internal readonly List<DeviceViewHandle> Views = new List<DeviceViewHandle>();
            private readonly List<DeviceViewHandle> fields = new List<DeviceViewHandle>();
            internal SimulationContext C => Simulation.Context;
            internal Rig(int pairCount = 1)
            {
                Scene = SceneManager.CreateScene("PortalValidation_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var balance = GameBalanceLoader.Load(); foreach (var d in balance.Devices) d.StartingCount = 0;
                balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 1000; balance.Marble.FireIntervalSeconds = balance.Marble.MaxFireIntervalSeconds = .01f;
                Simulation = new MarbleSimulation(balance); Bridge = new MarblePhysicsBridge(Scene, null, null, Simulation.EnqueueContact);
                Simulation.StartSession(Bridge);
                var definition = C.Balance.Devices.Single(d => d.Id == 103);
                for (int i = 0; i < pairCount; i++)
                    CampaignStateUtility.CreateDevice(C, definition, definition.InstallPrice);
                var round = C.Round; round.Phase = (byte)RoundPhase.Build; C.Round = round;
                Owned = Simulation.GetOwnedDevices().OrderBy(x => x.InstanceId).ToArray();
                Board = new GameObject("PortalBoard"); SceneManager.MoveGameObjectToScene(Board, Scene);
                Board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 47, 13));
                for (int i = 0; i < Owned.Length; i++)
                {
                    Check(Simulation.PlaceDevice(Owned[i].InstanceId, i * 2, 0, 0), "place each purchased portal independently");
                    var view = DeviceView.Create(Owned[i], Board.transform, C.DeviceScripts.Get(Owned[i].InstanceId, 103)); Views.Add(view);
                    view.Root.transform.localPosition = new Vector3(i * 2, 0, 0);
                    view.Root.transform.localRotation = Quaternion.Euler(0, i % 2 == 1 ? 130 : 0, 0);
                    view.Root.SetActive(true);
                    Check(view.Root.GetComponentInChildren<MeshCollider>().isTrigger, "portal package uses a non-solid cylinder sensor");
                }
                Owned = Simulation.GetOwnedDevices().OrderBy(x => x.InstanceId).ToArray();
                Physics.SyncTransforms(); Simulation.BeginRound();
                Simulation.ApplyGlobalAttribute(974, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
            }
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            internal Rigidbody Body(MarbleKey key) => Scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
            internal MarbleKey Launch(Vector3 localPosition, Vector3 localVelocity)
            {
                Simulation.FireOnce(); Simulation.BeforePhysics(Dt, Bridge, new Vector3(10, 5, 0), Quaternion.identity); Simulation.AfterPhysics(Bridge);
                var key = C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Body(key).drag = Body(key).angularDrag = 0;
                Check(Bridge.TryMove(key, Board.transform.TransformPoint(localPosition), Board.transform.rotation * localVelocity), "position fixture ball"); return key;
            }
            internal DeviceContactContext Api(MarbleKey key, int index)
            {
                CampaignStateUtility.FindDevice(C, Owned[index].InstanceId, out var device); Bridge.TryGetDevicePose(Owned[index].InstanceId, out var pose);
                return new DeviceContactContext(C, Ball(key), device, Views[index].Root.GetComponent<DeviceBody>().Contact(key, Body(key).position), pose);
            }
            internal void Contact(MarbleKey key, int index)
            {
                Simulation.BeforePhysics(Dt, Bridge, Vector3.up * 10, Quaternion.identity);
                Simulation.EnqueueContact(Views[index].Root.GetComponent<DeviceBody>().Contact(key, Body(key).position)); Simulation.AfterPhysics(Bridge);
            }
            internal MarblePhysicsState Step(MarbleKey key)
            {
                Simulation.BeforePhysics(Dt, Bridge, Vector3.up * 10, Quaternion.identity); Physics.SyncTransforms(); Bridge.Simulate(Dt);
                Bridge.TryGetState(key, out var before); Simulation.AfterPhysics(Bridge); return before;
            }
            internal Vector3 AddField(float x, float yaw)
            {
                var definition = C.Balance.Devices.Single(d => d.Id == 9004); CampaignStateUtility.CreateDevice(C, definition, definition.InstallPrice);
                var owned = Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last(); CampaignStateUtility.FindDevice(C, owned.InstanceId, out var entity);
                var data = C.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; C.Manager.SetComponentData(entity, data);
                var view = DeviceView.Create(owned, Board.transform, C.DeviceScripts.Get(owned.InstanceId, definition.Id)); fields.Add(view);
                view.Root.transform.localPosition = new Vector3(x, 0, 0); view.Root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                view.Root.SetActive(true); Physics.SyncTransforms(); return view.Root.transform.rotation * Vector3.back * definition.Strength;
            }
            public void Dispose()
            {
                foreach (var view in Views) view.Dispose(); foreach (var view in fields) view.Dispose();
                Simulation.Dispose(); Bridge.Dispose(); UnityEngine.Object.DestroyImmediate(Board); SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }
}
