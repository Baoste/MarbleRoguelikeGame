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
        private static void SandbagLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                var device = AddSandbag(f); var first = f.Launch(); var second = f.Launch();
                SandbagAt(f, 1, SandbagHit(f, first, device));
                Near(11, f.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE), "sandbag first hit adds one");
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("连击 1"), "combo status is visible");
                SandbagAt(f, 1.5, SandbagHit(f, second, device));
                Near(12, f.Simulation.GetMarbleAttribute(second, GameAttribute.BALL_BASE_VALUE), "another ball shares this instance's combo");
                SandbagAt(f, 2, SandbagHit(f, first, device));
                Near(14, f.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE), "same ball can hit again for combo three");
                double deadline = 2 + 3 / Math.Sqrt(3);
                SandbagTickAt(f, deadline - .001);
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("连击 3"), "combo persists until its shortened deadline");
                SandbagTickAt(f, deadline);
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("连击 0"), "combo resets at exact deadline without another hit");
                Near(14, f.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE), "timeout preserves previously earned value");
                SandbagAt(f, 4, SandbagHit(f, first, device));
                Near(15, f.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE), "hit after timeout restarts at one");
                // Contacts from distinct balls in the same step must not share a cooldown.
                SandbagAt(f, 4.5, SandbagHit(f, second, device), SandbagHit(f, first, device));
                Near(17, f.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE), "earlier ball in deterministic contact order gets combo two");
                Near(15, f.Simulation.GetMarbleAttribute(second, GameAttribute.BALL_BASE_VALUE), "second simultaneous ball gets combo three");
                Near(14, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "six effective hits spend six uses");
                var link = SandbagHit(f, first, device); link.LinkKey = 123;
                f.Step(f.Hit(first, device), link);
                Near(17, f.Simulation.GetMarbleAttribute(first, GameAttribute.BALL_BASE_VALUE), "trigger and link events do not count as solid impacts");
                Near(14, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "ignored contacts cost nothing");
                var other = AddSandbag(f);
                SandbagAt(f, 5, SandbagHit(f, second, other));
                Near(16, f.Simulation.GetMarbleAttribute(second, GameAttribute.BALL_BASE_VALUE), "another sandbag starts an independent combo");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == other.InstanceId).RemainingDurability, "another instance has independent durability");
                // The bonus modifies the ball, not the round score; normal terminal scoring still applies.
                Near(0, f.C.Round.Score, "sandbag never settles or awards the round score immediately");
                f.Step(new MarbleContact { Key = first, Kind = MarbleContactKind.Score, TargetId = 10, Multiplier = 1, RushDisabled = true });
                Near(17, f.C.Round.Score, "ordinary settlement includes accumulated sandbag bonus");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round;
                Check(f.Simulation.BeginRound(), "new round retains placed sandbags");
                var fresh = f.Launch(); f.Step(SandbagHit(f, fresh, device));
                Near(11, f.Simulation.GetMarbleAttribute(fresh, GameAttribute.BALL_BASE_VALUE), "new round clears old combo");
                Near(19, f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == device.InstanceId).RemainingDurability, "new round restores durability");
            }
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 9008); definition.Durability = -1;
                var device = AddSandbag(f); var key = f.Launch();
                f.Simulation.ApplyAttribute(key, 972, GameAttribute.BALL_LIFETIME, AttributeOperation.Set, 60);
                for (int i = 1; i <= 121; i++)
                {
                    f.Step(SandbagHit(f, key, device));
                    Near(10 + i * (i + 1) / 2, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "unlimited device keeps increasing each consecutive bonus");
                }
                Near(-1, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "unlimited combo never depletes durability");
                double last = f.C.Round.Time;
                SandbagTickAt(f, last + .299);
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("连击 121"), "high combo timeout stops shrinking below 0.3 seconds");
                SandbagTickAt(f, last + .301);
                Check(f.Simulation.GetDeviceStatus(device.InstanceId).Contains("连击 0"), "minimum timeout expires");
            }
            using (var f = new Fixture())
            {
                var device = AddSandbag(f); var key = f.Launch();
                for (int i = 1; i <= 21; i++)
                {
                    f.Step(SandbagHit(f, key, device));
                    int count = Math.Min(20, i);
                    Near(10 + count * (count + 1) / 2, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "twentieth hit applies and twenty-first is disabled");
                }
                Check(f.Simulation.GetOwnedDevices().Single().Exhausted && f.Simulation.GetDeviceStatus(device.InstanceId) == "", "exhausted sandbag has no stale active combo display");
            }
            SandbagAttributeApi();
            SandbagPhysics();
            Debug.Log("SANDBAG_VALIDATION_PASSED: " + (assertions - start) + " assertions; combo timing, per-instance state, simultaneous impacts, additive value, durability and real PhysX collision entries.");
        }

        private static OwnedDeviceData AddSandbag(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9008));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var entity);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(entity);
        }
        private static MarbleContact SandbagHit(Fixture f, MarbleKey key, OwnedDeviceData device)
        {
            var hit = f.Hit(key, device); hit.HasImpact = true; hit.Normal = Vector3.back; hit.IncomingVelocity = Vector3.forward * 2;
            return hit;
        }
        private static void SandbagAt(Fixture f, double time, params MarbleContact[] hits)
        { var state = f.C.Round; state.Time = time - .1f; f.C.Round = state; f.Step(hits); }
        private static void SandbagTickAt(Fixture f, double time)
        { var state = f.C.Round; state.Time = time; f.C.Round = state; DeviceScriptSystem.Tick(f.C); }

        private static void SandbagAttributeApi()
        {
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 9008); definition.Value = 2;
                var device = AddSandbag(f); var key = f.Launch();
                f.Simulation.ApplyAttribute(key, 973, GameAttribute.BALL_BASE_VALUE, AttributeOperation.Multiply, 2);
                f.Step(SandbagHit(f, key, device));
                Near(24, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "base addition applies current multiplier once, never bakes it into the base");
                f.Step(SandbagHit(f, key, device));
                Near(32, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "configured bonus scales linearly with combo under a modifier");
                f.C.Manager.GetBuffer<AttributeModifierData>(f.Ball(key)).Clear(); AttributeRuntime.Recompute(f.C, f.Ball(key));
                Near(16, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "removing multiplier leaves only earned base value");
                CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var entity);
                var api = new DeviceContactContext(f.C, f.Ball(key), entity, SandbagHit(f, key, device), f.Physics.Poses[device.InstanceId]);
                api.AddBase("BALL_BASE_VALUE", -1); Near(15, api.Value, "base addition independently supports negative deltas");
                ExpectArgument(() => api.AddBase("BALL_BASE_VALUE", double.NaN));
                ExpectArgument(() => api.AddBase("BALL_BASE_VALUE", double.PositiveInfinity));
                Near(15, api.Value, "invalid delta does not mutate base value");
                api.AddBase("BALL_BASE_VALUE", double.MaxValue);
                Near(f.C.Content.Definition(GameAttribute.BALL_BASE_VALUE).MaxValue, api.Value, "base addition obeys attribute cap");
                api.Close(); ExpectExpired(() => api.AddBase("BALL_BASE_VALUE", 1));
            }
        }

        private static void SandbagPhysics()
        {
            var scene = SceneManager.CreateScene("SandbagPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, contact => { if (contact.HasImpact) hits.Add(contact); simulation.EnqueueContact(contact); });
            var board = new GameObject("SandbagBoard"); SceneManager.MoveGameObjectToScene(board, scene);
            MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(974, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(975, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 0);
                var definition = c.Balance.Devices.Single(d => d.Id == 9008);
                CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                var owned = simulation.GetOwnedDevices().Single();
                CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, definition.Id));
                view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>();
                Check(collider != null && collider.convex && !collider.isTrigger, "sandbag package provides a solid cylinder");
                var spawn = board.transform.TransformPoint(new Vector3(0, .1f, .8f));
                simulation.FireOnce(); simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                var key = c.Marbles.Keys.Single();
                for (int pass = 0; pass < 2; pass++)
                {
                    Check(bridge.TryMove(key, spawn, board.transform.rotation * Vector3.back * 2), "set up an independent physical impact");
                    for (int steps = 0; hits.Count <= pass && steps < 50; steps++)
                    { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                    Check(hits.Count == pass + 1, "real collision entry registers once");
                    Near(pass == 0 ? 11 : 13, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "real collision runs packaged combo Lua");
                    for (int steps = 0; steps < 10; steps++)
                    { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                    Check(hits.Count == pass + 1, "resting collision does not generate extra combo hits");
                }
                Near(18, simulation.GetOwnedDevices().Single().RemainingDurability, "physical collision stay consumes no extra durability");
                Check(bridge.TryMove(key, spawn, Vector3.zero), "move clear of the source before timeout");
                for (int steps = 0; steps < 160; steps++)
                { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                Check(simulation.GetDeviceStatus(owned.InstanceId).Contains("连击 0"), "real fixed-step timer clears combo");
                Near(13, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "real timeout preserves ball value");
            }
            finally
            { view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
