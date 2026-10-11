using System;
using System.Collections.Generic;
using System.Linq;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void StoneBurstLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                var device = AddStoneBurst(f); var key = f.Launch();
                var pose = f.Physics.Poses[device.InstanceId];
                var rotation = Quaternion.Euler(50, 123, 17); pose.Forward = rotation * Vector3.back; pose.Up = rotation * Vector3.up;
                f.Physics.Poses[device.InstanceId] = pose;
                float blood = f.C.Player.Blood;
                var motion = f.Physics.States[key].LinearVelocity;
                for (int i = 1; i <= 7; i++)
                {
                    f.Step(SandbagHit(f, key, device));
                    Check(f.C.Marbles.Count == 1 && f.Simulation.GetDeviceStatus(device.InstanceId).Contains(i + "/8"), "each impact charges once without emitting early");
                    Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "charging does not consume durability");
                }
                f.Step(SandbagHit(f, key, device));
                var children = StoneChildren(f, device.InstanceId);
                Check(children.Length == 16 && f.C.Marbles.Count == 17, "eighth impact creates sixteen extra balls and retains input");
                VectorNear(motion, f.Physics.States[key].LinearVelocity, "charge never rewrites incoming ball motion");
                Near(10, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "input ball retains its value");
                Near(blood, f.C.Player.Blood, "burst costs no blood");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "whole burst costs one use");
                VerifyStoneLayout(f.C, children, pose);
                double fired = f.C.Round.Time;
                for (int i = 0; i < 4; i++) f.Step(SandbagHit(f, key, device));
                Check(f.C.Marbles.Count == 17 && f.Simulation.GetDeviceStatus(device.InstanceId).Contains("冷却"), "hits during cooldown do not charge or emit");
                SandbagAt(f, fired + 4.999, SandbagHit(f, key, device));
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("冷却"), "cooldown lasts full five seconds");
                SandbagAt(f, fired + 5, SandbagHit(f, key, device));
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("1/8"), "impact at cooldown boundary starts a new cycle");
                f.Step(SandbagHit(f, children[0], device));
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("1/8"), "own projectiles never recharge source");
                var other = AddStoneBurst(f);
                f.Step(SandbagHit(f, children[0], other));
                Check(f.Simulation.GetDeviceStatus(other.InstanceId).Contains("1/8"), "projectile may charge another independent device");
                var fresh = f.Launch();
                f.Step(SandbagHit(f, key, device), SandbagHit(f, fresh, device));
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("3/8"), "simultaneous different balls each charge");
                f.Step(SandbagHit(f, key, device), SandbagHit(f, key, device));
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("4/8"), "duplicate same-ball reports in one step do not double charge");
                f.Step(f.Hit(key, device));
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("4/8"), "trigger-only contacts do not charge");
                // Independent new balls settle using their configured value, not the triggering ball's multiplier.
                f.Step(new MarbleContact { Key = children[1], Kind = MarbleContactKind.Score, TargetId = 10, Multiplier = 1, RushDisabled = true });
                Near(1, f.C.Round.Score, "generated ball settles for its own value");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; f.Simulation.BeginRound(); f.Step();
                Check(f.C.Marbles.Count == 0 && f.Simulation.GetDeviceStatus(device.InstanceId).Contains("0/8"), "new round clears generated balls, charge and cooldown");
                Near(20, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == device.InstanceId).RemainingDurability, "new round restores durability");
            }
            StoneBurstFailures();
            SpawnBatchApi();
            StoneBurstPhysics();
            Debug.Log("STONE_BURST_VALIDATION_PASSED: " + (assertions - start) + " assertions; eight-hit charge, sixteen radial balls, cooldown, atomic rollback, source filtering and physical collisions.");
        }
        private static OwnedDeviceData AddStoneBurst(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9009));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static MarbleKey[] StoneChildren(Fixture f, int source) => f.C.Marbles.Keys.Where(k => f.C.Manager.GetComponentData<BallFeatures>(f.Ball(k)).SpawnSourceId == source).OrderBy(k => k.Sequence).ToArray();
        private static void VerifyStoneLayout(SimulationContext c, MarbleKey[] keys, DevicePose pose)
        {
            Vector3 total = Vector3.zero;
            for (int i = 0; i < keys.Length; i++)
            {
                var entity = c.Marbles[keys[i]]; var position = c.Manager.GetComponentData<Transform3D>(entity).Position;
                var velocity = c.Manager.GetComponentData<Motion3D>(entity).LinearVelocity; total += velocity;
                var expected = Quaternion.AngleAxis(i * 22.5f, pose.Up) * pose.Forward;
                VectorNear(expected * 6, velocity, "sixteen evenly spaced directions on tilted board");
                Near(1, AttributeRuntime.Get(c, entity, GameAttribute.BALL_BASE_VALUE), "each new ball has value one");
                Near(0, c.Manager.GetComponentData<ShotOrigin>(entity).BloodInvestment, "new ball has no invested blood");
                var features = c.Manager.GetComponentData<BallFeatures>(entity);
                Near(0, features.RefundBlood, "free ball cannot create blood through refund");
                float radius = (float)AttributeRuntime.Get(c, entity, GameAttribute.BALL_RADIUS) * AttributeRuntime.RadiusToWorld;
                Near(radius + .005f, Vector3.Dot(position - pose.Position, pose.Up), "spawn height clears board surface");
                Check(Vector3.ProjectOnPlane(position - pose.Position, pose.Up).magnitude > pose.Radius + radius, "each position clears the source");
                for (int j = 0; j < i; j++)
                    Check(Vector3.Distance(position, c.Manager.GetComponentData<Transform3D>(c.Marbles[keys[j]]).Position) >= radius * 2 + .019f, "radial spawn slots do not overlap");
            }
            VectorNear(Vector3.zero, total, "radial velocity distribution is symmetric");
        }
        private static void StoneBurstFailures()
        {
            foreach (bool failPhysics in new[] { false, true })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9009).Durability = 1;
                var device = AddStoneBurst(f); var key = f.Launch();
                for (int i = 0; i < 7; i++) f.Step(SandbagHit(f, key, device));
                if (failPhysics) f.Physics.FailSpawnAt = f.Physics.Attempts + 9;
                else f.C.Tuning.MaxActiveMarbles = 16;
                f.Step(SandbagHit(f, key, device));
                Check(f.C.Marbles.Count == 1 && f.Physics.Spawns.Count == 1 && f.C.Round.ActiveMarbleCount == 1, "blocked or capped batch leaves no partial entities or bodies");
                Near(1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "failed emission does not spend durability");
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("等待发射"), "full charge survives a failed emission");
                f.Physics.FailSpawnAt = 0; f.C.Tuning.MaxActiveMarbles = 256;
                f.Step(); f.Step();
                Check(StoneChildren(f, device.InstanceId).Length == 16 && f.C.Round.ActiveMarbleCount == 17, "timer retries without needing a ninth hit");
                Check(f.Simulation.GetOwnedDevices().Single().Exhausted, "last use emits a full batch before exhaustion");
                f.Step(SandbagHit(f, key, device));
                Check(f.C.Marbles.Count == 17, "exhausted source cannot emit or recharge");
            }
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9009).Durability = -1;
                var device = AddStoneBurst(f); var key = f.Launch();
                f.Simulation.ApplyAttribute(key, 971, GameAttribute.BALL_LIFETIME, AttributeOperation.Set, 600);
                for (int cycle = 0; cycle < 21; cycle++)
                {
                    SandbagTickAt(f, cycle * 6.0);
                    for (int i = 0; i < 8; i++) f.Step(SandbagHit(f, key, device));
                    var emitted = StoneChildren(f, device.InstanceId);
                    Check(emitted.Length == 16, "unlimited device can emit more than twenty cycles");
                    foreach (var child in emitted) ContentSpawnSystem.Rollback(f.C, f.Ball(child));
                }
                Near(-1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "unlimited durability is unchanged");
            }
        }
        private static void SpawnBatchApi()
        {
            using (var f = new Fixture())
            {
                var device = AddStoneBurst(f); CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var entity);
                var api = new DeviceEventContext(f.C, entity); var batch = api.CreateSpawnBatch();
                batch.TryAdd(Vector3.one, Vector3.right, 1);
                Check(!batch.TryCommit() && f.C.Marbles.Count == 0, "spawn before first timestep is rejected without allocating a ball"); api.Close();
                f.Step(); api = new DeviceEventContext(f.C, entity); batch = api.CreateSpawnBatch();
                Check(!batch.TryAdd(Vector3.zero, Vector3.zero, 1) && !batch.TryAdd(Vector3.zero, Vector3.one, double.NaN), "invalid spawn request rejected");
                Check(batch.TryAdd(Vector3.zero, Vector3.right * 200, 3), "arbitrary nonradial position and velocity accepted");
                Check(!batch.TryAdd(Vector3.zero, Vector3.right, 2), "overlapping batch requests rejected");
                Check(batch.TryAdd(Vector3.up * 2, Vector3.back * 6, 2) && batch.Count == 2, "different positions and values compose independently");
                Check(batch.TryCommit() && !batch.TryCommit(), "batch commits at most once");
                Check(!batch.TryAdd(Vector3.up * 4, Vector3.one, 1), "submitted batch cannot be appended");
                var keys = StoneChildren(f, device.InstanceId);
                Near(f.C.Content.Definition(GameAttribute.BALL_MAX_SPEED).DefaultValue, f.Physics.States[keys[0]].LinearVelocity.magnitude, "spawn obeys configured speed cap");
                Near(20, api.RemainingDurability, "spawn primitive does not implicitly consume durability");
                var vectors = api.Vectors;
                VectorNear(Vector3.right * 3, vectors.Rotate(Vector3.forward * 3, Vector3.up * 4, 90), "generic rotate preserves magnitude and normalizes its axis");
                ExpectArgument(() => vectors.Rotate(Vector3.forward, Vector3.zero, 90));
                ExpectArgument(() => vectors.Rotate(Vector3.forward, Vector3.up, float.NaN));
                api.Close(); ExpectExpired(() => batch.TryCommit()); ExpectExpired(() => { _ = batch.Radius; }); ExpectExpired(() => vectors.Rotate(Vector3.one, Vector3.up, 1));
                api = new DeviceEventContext(f.C, entity); batch = api.CreateSpawnBatch();
                batch.TryAdd(Vector3.one * 3, Vector3.forward, 1);
                var owned = f.C.Manager.GetComponentData<OwnedDeviceData>(entity); owned.Placed = false; f.C.Manager.SetComponentData(entity, owned);
                Check(!batch.TryCommit() && f.C.Marbles.Count == 2, "recalled source cannot submit new balls"); api.Close();
            }
        }
        private static void StoneBurstPhysics()
        {
            var scene = SceneManager.CreateScene("StoneBurstPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("StoneBurstBoard"); SceneManager.MoveGameObjectToScene(board, scene);
            MResLoadHandle view = null; GameObject obstacle = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(966, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var definition = c.Balance.Devices.Single(d => d.Id == 9009);
                CampaignStateUtility.CreateDevice(c, definition, definition.Price); var owned = simulation.GetOwnedDevices().Single();
                CampaignStateUtility.FindDevice(c, owned.InstanceId, out var device);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(device); data.Placed = true; c.Manager.SetComponentData(device, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(50, 77, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, definition.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                Check(!view.Root.GetComponentInChildren<MeshCollider>().isTrigger, "stone cylinder is a solid impact target");
                var spawn = board.transform.TransformPoint(new Vector3(0, .085f, 1));
                simulation.FireOnce(); simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                var key = c.Marbles.Keys.Single();
                bridge.TryGetDevicePose(owned.InstanceId, out var pose);
                float radius = (float)c.Content.Definition(GameAttribute.BALL_RADIUS).DefaultValue * AttributeRuntime.RadiusToWorld;
                float distance = (radius * 2 + .02f) / (2 * Mathf.Sin(Mathf.PI / 16));
                obstacle = new GameObject("BlockedBurstSlot"); SceneManager.MoveGameObjectToScene(obstacle, scene);
                obstacle.transform.position = pose.Position + pose.Up * (radius + .005f) + pose.Forward * distance;
                obstacle.AddComponent<SphereCollider>().radius = .01f; Physics.SyncTransforms();
                for (int n = 0; n < 8; n++)
                {
                    Check(bridge.TryMove(key, spawn, board.transform.rotation * Vector3.back * 4), "prepare repeated real impact");
                    for (int steps = 0; hits.Count <= n && steps < 30; steps++)
                    { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                    Check(hits.Count == n + 1, "real collision charges exactly once");
                }
                Check(c.Marbles.Count == 1 && bridge.ActiveBodyCount == 1, "solid obstruction rejects entire radial batch");
                Near(20, simulation.GetOwnedDevices().Single().RemainingDurability, "physical spawn rejection costs no durability");
                UnityEngine.Object.DestroyImmediate(obstacle); obstacle = null;
                Check(bridge.TryMove(key, pose.Position + pose.Up * 2, Vector3.zero), "move input clear of radial slots"); Physics.SyncTransforms();
                // Retry happens in OnTick before physics; inspect pristine output positions before integrating gravity.
                for (int steps = 0; c.Marbles.Count == 1 && steps < 12; steps++)
                { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); }
                var children = c.Marbles.Keys.Where(k => !k.Equals(key)).OrderBy(k => k.Sequence).ToArray();
                Check(children.Length == 16 && bridge.ActiveBodyCount == 17, "clearing obstacle lets the full physical batch spawn");
                VerifyStoneLayout(c, children, pose);
                Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "successful physical retry costs one use");
            }
            finally
            { if (obstacle != null) UnityEngine.Object.DestroyImmediate(obstacle); view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
