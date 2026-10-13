using System;
using System.Collections.Generic;
using System.Linq;
using MRes;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static OwnedDeviceData AddDuplicator(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9018));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var e);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(e);
        }
        private static MarbleContact CopyHit(Fixture f, MarbleKey key, OwnedDeviceData device)
        {
            var hit = f.Hit(key, device); hit.HasImpact = true;
            hit.Normal = Vector3.back; hit.IncomingVelocity = new Vector3(3, 0, 4); return hit;
        }
        private static void DuplicatorLuaApi()
        {
            int start = assertions;
            foreach (int wear in new[] { 1, 20, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9018).Durability = wear;
                var device = AddDuplicator(f); var key = f.Launch(); var parent = f.Ball(key);
                f.Simulation.ApplyAttribute(key, 980, GameAttribute.BALL_BASE_VALUE, AttributeOperation.Add, 3);
                f.Simulation.ApplyAttribute(key, 981, GameAttribute.BALL_RADIUS, AttributeOperation.Set, .7);
                f.Simulation.ApplyAttribute(key, 982, GameAttribute.BALL_MASS, AttributeOperation.Set, 2);
                var api = CashApi(f, key, device); api.AddScoreMultiplier(1.5); api.AddScoreMultiplier(1.5); api.Close();
                var score = f.C.Manager.GetComponentData<MarbleScore>(parent); score.ScoreBonus = 7; f.C.Manager.SetComponentData(parent, score);
                f.C.Manager.GetBuffer<MarbleModifier>(parent).Add(new MarbleModifier { Attribute = MarbleModifierAttribute.ScoreAdd, Value = 9, Stacks = 1 });
                var before = f.Physics.States[key]; double blood = f.C.Player.Blood;
                long expiry = f.C.Manager.GetComponentData<Lifetime>(parent).ExpireTick;
                f.Step(CopyHit(f, key, device), CopyHit(f, key, device));
                Check(f.C.Marbles.Count == 2 && f.C.Marbles.ContainsKey(key), "one impact creates one copy and retains original");
                var copyKey = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); var copy = f.Ball(copyKey);
                Near(2, f.C.Round.ActiveMarbleCount, "copy increments active count once"); Near(blood, f.C.Player.Blood, "copy neither charges nor refunds blood");
                VectorNear(before.Position, f.Physics.States[key].Position, "copy does not move original");
                VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "copy leaves original post-solver bounce untouched");
                VectorNear(new Vector3(3, 0, -4), f.Physics.States[copyKey].LinearVelocity, "copy mirrors entry direction and retains entry speed");
                Near(0, f.Simulation.GetMarbleAttribute(copyKey, GameAttribute.BALL_BASE_VALUE), "copy begins at zero value");
                Near(13, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "original value is unchanged");
                Near(.7, f.Simulation.GetMarbleAttribute(copyKey, GameAttribute.BALL_RADIUS), "copy keeps modified size");
                Near(2, f.Simulation.GetMarbleAttribute(copyKey, GameAttribute.BALL_MASS), "copy keeps modified mass");
                Near(2, f.C.Manager.GetComponentData<MarbleScore>(copy).ScoreMultiplier, "copy inherits additive multipliers");
                Near(0, f.C.Manager.GetComponentData<MarbleScore>(copy).ScoreBonus, "zero override removes flat score");
                Check(f.C.Manager.GetBuffer<MarbleModifier>(copy).Length == 0, "zero override removes inherited flat modifiers");
                Near(expiry, f.C.Manager.GetComponentData<Lifetime>(copy).ExpireTick, "copy does not restart lifespan");
                Check(f.C.Manager.GetComponentData<BallFeatures>(copy).Family == f.C.Manager.GetComponentData<BallFeatures>(parent).Family, "copy shares refund family");
                var query = CashApi(f, copyKey, device); Check(query.HasCopiedFrom(device.InstanceId), "copy records its source"); query.Close();
                f.Step(CopyHit(f, key, device), CopyHit(f, copyKey, device));
                Near(2, f.C.Marbles.Count, "original repeat and own descendant do not duplicate");
                Near(wear == -1 ? -1 : wear - 1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "only successful copy spends durability");
                Near(0, f.Simulation.GetMarbleAttribute(copyKey, GameAttribute.BALL_BASE_VALUE), "next attribute tick does not restore copied value bonuses");
                f.Step(new MarbleContact { Key = copyKey, TargetId = 700, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true });
                Near(0, f.C.Round.Score + f.C.Round.FractionalScore, "untouched zero copy settles for zero even with inherited multiplier");
                f.Step(new MarbleContact { Key = key, TargetId = 700, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true });
                Near(58, f.C.Round.Score + f.C.Round.FractionalScore, "original value and flat bonuses are untouched");
            }
            using (var f = new Fixture())
            {
                var device = AddDuplicator(f); var sandbag = f.Add(f.C.Balance.Devices.Single(d => d.Id == 9008)); var key = f.Launch();
                f.Step(CopyHit(f, key, sandbag)); // The original has already visited the device.
                f.Step(CopyHit(f, key, device)); var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                f.Step(CopyHit(f, child, sandbag));
                Near(2, f.Simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "new copy can trigger previously visited other device and gain value");
                var query = CashApi(f, child, sandbag); Check(query.HasTriggeredOtherDevice, "copy preserves successful history for other device effects"); query.Close();
                var other = AddDuplicator(f); f.Step(CopyHit(f, child, other));
                Near(3, f.C.Marbles.Count, "copy can activate a different duplicator instance");
                var grandchild = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); query = CashApi(f, grandchild, device);
                Check(query.HasCopiedFrom(device.InstanceId) && query.HasCopiedFrom(other.InstanceId), "copy ancestry is transitive"); query.Close();
                f.Step(CopyHit(f, grandchild, device)); Near(3, f.C.Marbles.Count, "grandchild cannot feed ancestor");
                var splitter = AddSplitter(f); f.Step(f.Hit(grandchild, splitter));
                var split = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); query = CashApi(f, split, device);
                Check(query.HasCopiedFrom(device.InstanceId) && query.HasCopiedFrom(other.InstanceId), "division preserves copy ancestry");
                query.Children(1, false); query.Close();
                var legacy = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); query = CashApi(f, legacy, device);
                Check(query.HasCopiedFrom(device.InstanceId), "legacy copies preserve copy ancestry"); query.Close();
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "start next copy round");
                var fresh = f.Launch(); f.Step(CopyHit(f, fresh, device)); Near(2, f.C.Marbles.Count, "new round clears Lua per-ball tracking");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == device.InstanceId).RemainingDurability, "new round restores wear");
            }
            foreach (bool capacity in new[] { false, true })
            using (var f = new Fixture())
            {
                var device = AddDuplicator(f); var key = f.Launch(); var hit = CopyHit(f, key, device);
                if (capacity) f.C.Tuning.MaxActiveMarbles = 1; else f.Physics.FailSpawnAt = f.Physics.Attempts + 1;
                f.Step(hit); Near(1, f.C.Marbles.Count, "failed copy keeps original only"); Near(1, f.C.Round.ActiveMarbleCount, "failed copy rolls back active count");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "failed copy costs no durability");
                f.C.Tuning.MaxActiveMarbles = 2; f.Physics.FailSpawnAt = 0; f.Step(hit);
                Near(2, f.C.Marbles.Count, "same original can retry on later collision after failure");
            }
            using (var f = new Fixture())
            {
                var device = AddDuplicator(f); var key = f.Launch();
                f.Step(f.Hit(key, device)); var hit = CopyHit(f, key, device); hit.IncomingVelocity = hit.Normal; f.Step(hit);
                hit.IncomingVelocity = Vector3.zero; f.Step(hit); hit.IncomingVelocity = Vector3.forward; hit.Normal = Vector3.up; f.Step(hit);
                Near(1, f.C.Marbles.Count, "trigger-only, separating, zero-speed and top contacts do not copy");
                var api = CashApi(f, key, device); var copy = api.CreateCopy();
                Check(!copy.TrySetValue(double.NaN) && !copy.TrySetValue(-1), "copy override rejects invalid values");
                Check(copy.TrySetValue(4), "copy API supports arbitrary valid value independent of device"); Near(4, copy.Value, "configured copy value");
                Check(!copy.TrySpawn(Vector3.one * float.NaN, Vector3.zero), "invalid position rejected");
                Check(!copy.TrySpawn(Vector3.one, Vector3.zero), "copy request is single use even on failure");
                api.Close(); ExpectExpired(() => { _ = copy.Value; }); ExpectExpired(() => { _ = copy.Radius; });
                ExpectExpired(() => copy.TrySpawn(Vector3.one, Vector3.zero)); ExpectExpired(() => copy.TrySetValue(0));
                api = CashApi(f, key, device); copy = api.CreateCopy();
                Check(copy.TrySpawn(Vector3.one * 3, Vector3.zero), "generic copy supports zero velocity and default inherited value"); api.Close();
                var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Near(10, f.Simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "without override value is inherited");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "copy primitive does not implicitly spend durability");
                var flags = f.C.Manager.GetComponentData<BallFeatures>(f.Ball(child)); flags.ReplicationGeneration = f.C.Content.MaxSplitGeneration; f.C.Manager.SetComponentData(f.Ball(child), flags);
                api = CashApi(f, child, device); Check(!api.CreateCopy().CanCopy, "generic copy respects replication generation cap"); api.Close();
            }
            foreach (float dt in new[] { .01f, .02f }) foreach (float offset in new[] { 0f, .12f }) DuplicatorPhysics(dt, offset);
            Debug.Log("DUPLICATOR_VALIDATION_PASSED: " + (assertions - start) + " assertions; mirror direction, zero value, other devices, ancestry, rollback, wear, callback scope and real PhysX collisions.");
        }
        private static void DuplicatorPhysics(float dt, float offset)
        {
            var scene = SceneManager.CreateScene("DuplicatorPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, hit => { if (hit.HasImpact) hits.Add(hit); simulation.EnqueueContact(hit); });
            var board = new GameObject("DuplicatorBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(987, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(988, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 1);
                simulation.ApplyGlobalAttribute(989, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var definition = c.Balance.Devices.Single(d => d.Id == 9018); CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, definition.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>(); Check(collider != null && collider.convex && !collider.isTrigger, "copy model has solid cylindrical surface");
                var spawn = board.transform.TransformPoint(new Vector3(offset, .1f, .8f));
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0; var incoming = board.transform.rotation * Vector3.back * 2; bridge.TrySetVelocity(key, incoming);
                Vector3 originalAfterSolver = default;
                for (int n = 0; hits.Count == 0 && n < 100; n++)
                {
                    simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); originalAfterSolver = body.velocity; simulation.AfterPhysics(bridge);
                }
                Check(hits.Count > 0 && c.Marbles.Count == 2 && bridge.ActiveBodyCount == 2, "real collision successfully spawns non-overlapping copy");
                var child = c.Marbles.Keys.OrderBy(k => k.Sequence).Last(); Check(bridge.TryGetState(child, out var state), "copy has physical body");
                var normal = Vector3.ProjectOnPlane(hits[0].Normal, board.transform.up).normalized;
                VectorNear(Vector3.Reflect(incoming, normal), state.LinearVelocity, "real copy uses pre-solver mirror velocity on tilted board");
                VectorNear(originalAfterSolver, body.velocity, "copy leaves real original physics unchanged");
                Check(Vector3.Distance(body.position, state.Position) > .16f, "copy spawns clear of original");
                Near(0, simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "physical copy is zero value");
                Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "real collision spends exactly one use");
                ContentSpawnSystem.Rollback(c, c.Marbles[key]);
                Check(bridge.TryMove(child, spawn, Vector3.zero), "prepare copy to revisit its source");
                simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge);
                bridge.TrySetVelocity(child, incoming); int prior = hits.Count;
                for (int n = 0; hits.Count == prior && n < 100; n++)
                { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                Check(hits.Count > prior && c.Marbles.Count == 1, "descendant still collides physically but does not duplicate");
                Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "descendant collision is free");
            }
            finally { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
