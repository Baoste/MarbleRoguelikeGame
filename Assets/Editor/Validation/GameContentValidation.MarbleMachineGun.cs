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
        private static OwnedDeviceData AddMachineGun(Fixture f)
        {
            f.Add(f.C.Balance.Devices.Single(d => d.Id == 9020));
            var latest = f.Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
            CampaignStateUtility.FindDevice(f.C, latest.InstanceId, out var e);
            return f.C.Manager.GetComponentData<OwnedDeviceData>(e);
        }
        private static void MarbleMachineGunLuaApi()
        {
            int start = assertions;
            foreach (double input in new[] { 0, .5, 1, 1.5, 5.5, 12 })
            using (var f = new Fixture())
            {
                var device = AddMachineGun(f); var key = f.Launch(); var parent = f.Ball(key);
                AttributeRuntime.SetBase(f.C, parent, GameAttribute.BALL_BASE_VALUE, input);
                var api = CashApi(f, key, device); api.AddScoreMultiplier(1.5); api.AddScoreMultiplier(1.5); api.Close();
                var score = f.C.Manager.GetComponentData<MarbleScore>(parent); score.ScoreBonus = input; f.C.Manager.SetComponentData(parent, score);
                f.C.Manager.GetBuffer<MarbleModifier>(parent).Add(new MarbleModifier { Attribute = MarbleModifierAttribute.ScoreAdd, Value = input / 2, Stacks = 1 });
                double blood = f.C.Player.Blood; long expiry = f.C.Manager.GetComponentData<Lifetime>(parent).ExpireTick;
                f.Step(f.Hit(key, device), f.Hit(key, device));
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "admission is free");
                int count = (int)Math.Floor(input);
                if (count == 0)
                {
                    Check(!f.Physics.Suspended.Contains(key), "less than one value passes through without being stored");
                    Near(input, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "unconvertible input retains value"); continue;
                }
                Check(f.Physics.Suspended.Contains(key) && f.Physics.Hidden.Contains(key), "input is stored and hidden");
                var angles = new List<float>();
                for (int i = 0; i < count; i++)
                {
                    f.Step(); var childKey = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); var child = f.Ball(childKey);
                    Near(i + 1, f.C.Marbles.Keys.Count(k => !k.Equals(key)), "exactly one new ball per interval");
                    Near(1, f.Simulation.GetMarbleAttribute(childKey, GameAttribute.BALL_BASE_VALUE), "every output is worth one");
                    Near(.04, f.Physics.Spawns[childKey].Quote.Radius, "small output world radius"); Near(.1, f.Physics.Spawns[childKey].Quote.Mass, "small output mass");
                    Near(6, f.Physics.States[childKey].LinearVelocity.magnitude, "configured output speed");
                    VectorNear(f.Physics.Poses[device.InstanceId].Forward * 6, f.Physics.States[childKey].LinearVelocity, "visible muzzle matches shot direction");
                    float angle = Vector3.SignedAngle(Vector3.forward, f.Physics.States[childKey].LinearVelocity, Vector3.up); angles.Add(angle);
                    Check(Math.Abs(angle) <= 22.501f, "shot stays within total 45 degree fan");
                    Near(2, f.C.Manager.GetComponentData<MarbleScore>(child).ScoreMultiplier, "additive multiplier is inherited unchanged");
                    Near(1, f.C.Manager.GetComponentData<MarbleScore>(child).ScoreBonus, "flat bonus is apportioned rather than duplicated per bullet");
                    Near(expiry, f.C.Manager.GetComponentData<Lifetime>(child).ExpireTick, "output keeps original lifetime");
                    api = CashApi(f, childKey, device); Check(api.HasSplitFrom(device.InstanceId), "output records its split ancestor"); api.Close();
                    Near(i + 1 == count ? 19 : 20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "only completed batch spends one durability");
                    if (i + 1 < count) Near(input - i - 1, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "remaining input value debited only for successful outputs");
                }
                Check(!f.C.Marbles.ContainsKey(key) && f.C.Marbles.Count == count, "last output consumes original and discards fractional remainder");
                Near(blood, f.C.Player.Blood, "conversion does not charge or refund blood");
                if (count >= 12) Check(angles.Max() > 22 && angles.Min() < -22 && angles[7] < angles[5] && angles[11] > angles[10], "fan sweeps to both ends and reverses repeatedly");
                var children = f.C.Marbles.Keys.ToArray(); f.Step(children.Select(k => f.Hit(k, device)).ToArray());
                Check(f.Physics.Suspended.Count == 0 && f.C.Marbles.Count == count, "own descendants pass through without being recaptured");
                f.Step(children.Select(k => new MarbleContact { Key = k, TargetId = 700, Kind = MarbleContactKind.Score, Multiplier = 1, RushDisabled = true }).ToArray());
                Near(count * 5, f.C.Round.Score + f.C.Round.FractionalScore, "remaining value and proportional bonuses settle once with inherited multiplier");
            }
            using (var f = new Fixture())
            {
                var device = AddMachineGun(f); var key = f.Launch(); var other = f.Launch();
                AttributeRuntime.SetBase(f.C, f.Ball(key), GameAttribute.BALL_BASE_VALUE, 3.5);
                f.Step(f.Hit(key, device)); f.Physics.FailSpawnAt = f.Physics.Attempts + 1;
                f.Step(f.Hit(other, device)); Near(3.5, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "failed first shot preserves full value");
                Check(f.Physics.Suspended.Contains(key) && !f.Physics.Suspended.Contains(other), "busy device passes other ball through");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "blocked output costs nothing");
                f.Physics.FailSpawnAt = 0; f.Step(); Near(2.5, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "retry succeeds without duplicate value");
                f.C.Tuning.MaxActiveMarbles = f.C.Round.ActiveMarbleCount; f.Step(); Near(2.5, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "capacity waits without spending remaining value");
                f.C.Tuning.MaxActiveMarbles = 100; f.Step(); f.Step();
                Check(!f.C.Marbles.ContainsKey(key) && f.C.Marbles.Count == 4, "retry completes three bullets plus untouched passing ball");
                Near(19, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "entire retried batch costs one use");
            }
            foreach (int wear in new[] { 1, -1 })
            using (var f = new Fixture())
            {
                f.C.Balance.Devices.Single(d => d.Id == 9020).Durability = wear;
                var device = AddMachineGun(f); var key = f.Launch(); AttributeRuntime.SetBase(f.C, f.Ball(key), GameAttribute.BALL_BASE_VALUE, 5);
                f.Step(f.Hit(key, device)); for (int i = 0; i < 5; i++) f.Step();
                Near(5, f.C.Marbles.Count, "last durability completes all five shots"); Near(wear == -1 ? -1 : 0, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "finite and unlimited wear");
                var round = f.C.Round; round.Phase = (byte)RoundPhase.Build; f.C.Round = round; Check(f.Simulation.BeginRound(), "start another gun round");
                var fresh = f.Launch(); AttributeRuntime.SetBase(f.C, f.Ball(fresh), GameAttribute.BALL_BASE_VALUE, 1); f.Step(f.Hit(fresh, device)); f.Step();
                Check(!f.C.Marbles.ContainsKey(fresh) && f.C.Marbles.Count == 1, "round reset clears pending burst state");
            }
            using (var f = new Fixture())
            {
                var device = AddMachineGun(f); var key = f.Launch(); var velocity = f.Physics.States[key].LinearVelocity;
                f.Step(f.Hit(key, device)); f.Step();
                CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var e); var data = f.C.Manager.GetComponentData<OwnedDeviceData>(e); data.Placed = false; f.C.Manager.SetComponentData(e, data);
                f.Step(); Check(!f.Physics.Suspended.Contains(key) && !f.Physics.Hidden.Contains(key), "removing source restores remainder visibility and motion");
                VectorNear(velocity, f.Physics.States[key].LinearVelocity, "cancelled remainder resumes original velocity");
                Near(9, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "cancelled remainder excludes the already emitted shot");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "cancelled unfinished batch costs no wear");
            }
            using (var f = new Fixture())
            {
                var device = AddMachineGun(f); var key = f.Launch();
                f.Step(f.Hit(key, device)); f.Simulation.ApplyAttribute(key, 970, GameAttribute.BALL_LIFETIME, AttributeOperation.Set, 1);
                for (int n = 0; n < 15 && f.C.Marbles.ContainsKey(key); n++) f.Step();
                Check(!f.C.Marbles.ContainsKey(key) && f.Physics.Hidden.Count == 0, "expired input cancels remaining sequence and visibility");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "expired batch does not spend durability");
            }
            PortionApiValidation();
            using (var f = new Fixture())
            {
                var device = AddMachineGun(f); var key = f.Launch();
                AttributeRuntime.SetBase(f.C, f.Ball(key), GameAttribute.BALL_RADIUS, 3);
                var state = f.Physics.States[key]; state.Position = f.Physics.Poses[device.InstanceId].Position + Vector3.forward * .68f;
                f.Physics.States[key] = state; f.Step(f.Hit(key, device)); f.Step();
                var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                Check((f.Physics.States[child].Position - state.Position).magnitude > .52f, "large input is clear of the emitted bullet");
            }
            foreach (float dt in new[] { .01f, .02f }) MachineGunPhysics(dt);
            Debug.Log("MARBLE_MACHINE_GUN_VALIDATION_PASSED: " + (assertions - start) + " assertions; paced fan, remainder, multipliers, atomic portions, retries, wear, lifecycle and real physics.");
        }
        private static void PortionApiValidation()
        {
            using (var f = new Fixture())
            {
                var device = AddMachineGun(f); var key = f.Launch(); var api = CashApi(f, key, device);
                ExpectArgument(() => api.CreatePortion(0)); ExpectArgument(() => api.CreatePortion(double.NaN));
                var part = api.CreatePortion(11); Check(!part.CanSplit, "cannot transfer more value than parent holds");
                part = api.CreatePortion(2.5); Check(!part.TrySetSize(float.NaN, 1) && !part.TrySetSize(.04f, .001f), "invalid physical sizes are rejected");
                Check(part.TrySetSize(.04f, .1f), "independent configurable portion size");
                f.Physics.FailSpawnAt = f.Physics.Attempts + 1; Check(!part.TrySpawn(Vector3.one * 3, Vector3.forward), "spawn failure is atomic");
                Near(10, api.Value, "spawn failure preserves parent"); Check(!part.TrySpawn(Vector3.one, Vector3.forward), "request submits only once");
                api.Close(); ExpectExpired(() => { _ = part.Value; }); ExpectExpired(() => { _ = part.CanSplit; }); ExpectExpired(() => part.TrySetSize(.04f, .1f));
                f.Physics.FailSpawnAt = 0; api = CashApi(f, key, device); api.SetCurrent("BALL_BASE_VALUE", 2.5);
                f.C.Tuning.MaxActiveMarbles = 1; part = api.CreatePortion(2.5);
                Check(part.TrySpawn(Vector3.one * 3, Vector3.zero), "final portion uses replacement capacity and supports zero speed");
                api.Close(); f.Step(); Near(1, f.C.Marbles.Count, "full transfer leaves one ball at exact capacity");
                var child = f.C.Marbles.Keys.Single(); Near(2.5, f.Simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "generic portion can transfer fractional value");
                Near(20, f.Simulation.GetOwnedDevices().Single().RemainingDurability, "portion primitive has no implicit wear");
            }
            using (var f = new Fixture())
            {
                var device = AddMachineGun(f); var key = f.Launch();
                f.Simulation.ApplyAttribute(key, 971, GameAttribute.BALL_BASE_VALUE, AttributeOperation.Multiply, 2);
                var api = CashApi(f, key, device); var part = api.CreatePortion(1);
                Check(part.TrySpawn(Vector3.one * 3, Vector3.right), "portion accepts value with active modifiers"); api.Close(); f.Step();
                Near(19, f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "value modifiers cannot regenerate transferred value next tick");
                var child = f.C.Marbles.Keys.OrderBy(k => k.Sequence).Last(); var splitter = AddSplitter(f); f.Step(f.Hit(child, splitter));
                foreach (var k in f.C.Marbles.Keys.Where(k => !k.Equals(key)))
                { api = CashApi(f, k, device); Check(api.HasSplitFrom(device.InstanceId), "later splits retain gun ancestry"); api.Close(); }
            }
        }
        private static void MachineGunPhysics(float dt)
        {
            var scene = SceneManager.CreateScene("MachineGunPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var simulation = new MarbleSimulation(Balance()); var bridge = new MarblePhysicsBridge(scene, null, null, simulation.EnqueueContact);
            var board = new GameObject("MachineGunBoard"); SceneManager.MoveGameObjectToScene(board, scene); MResLoadHandle view = null; GameObject obstacle = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(972, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                var def = c.Balance.Devices.Single(d => d.Id == 9020); CampaignStateUtility.CreateDevice(c, def, def.Price);
                var owned = simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(c, owned.InstanceId, out var entity);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; c.Manager.SetComponentData(entity, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, def.Id)); view.Root.SetActive(true); Physics.SyncTransforms();
                Check(view.Root.GetComponentsInChildren<Collider>().All(x => x.isTrigger), "machine gun has no solid collider to block passing balls");
                Check(bridge.TryGetDevicePose(owned.InstanceId, out var pose), "machine gun pose available"); var down = BallInteractionSystem.Down(pose).normalized;
                var spawn = pose.Position + pose.Up * .12f - down * .8f;
                void Step() { simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); bridge.Simulate(dt); simulation.AfterPhysics(bridge); }
                simulation.FireOnce(); simulation.BeforePhysics(dt, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge); var key = c.Marbles.Keys.Single();
                AttributeRuntime.SetBase(c, c.Marbles[key], GameAttribute.BALL_BASE_VALUE, 12.5); Check(bridge.TrySetVelocity(key, down * 3), "approach real entrance");
                for (int n = 0; !c.Manager.HasComponent<HeldMotion>(c.Marbles[key]) && n < 100; n++) Step();
                Check(c.Manager.HasComponent<HeldMotion>(c.Marbles[key]), "real trigger stores input");
                var parentBody = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key));
                Check(parentBody.Body.isKinematic && parentBody.GetComponent<Renderer>().forceRenderingOff, "stored input is suspended and hidden");
                obstacle = new GameObject("BlockedFan"); SceneManager.MoveGameObjectToScene(obstacle, scene);
                obstacle.transform.SetPositionAndRotation(pose.Position + down * .51f + pose.Up * .045f, Quaternion.LookRotation(down, pose.Up));
                obstacle.AddComponent<BoxCollider>().size = new Vector3(.6f, .3f, .2f); Physics.SyncTransforms();
                for (int n = 0; n < (int)Math.Ceiling(.35 / dt); n++) Step();
                Near(1, c.Marbles.Count, "blocked fan creates no balls"); Near(12.5, simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE), "blocked fan preserves all value");
                Near(20, simulation.GetOwnedDevices().Single().RemainingDurability, "blocked fan is free");
                UnityEngine.Object.DestroyImmediate(obstacle); obstacle = null;
                var seen = new HashSet<MarbleKey> { key }; var times = new List<double>(); var angles = new List<float>();
                for (int n = 0; c.Marbles.ContainsKey(key) && n < 300; n++)
                {
                    Step();
                    foreach (var child in c.Marbles.Keys.Where(k => !seen.Contains(k)).ToArray())
                    {
                        seen.Add(child); times.Add(c.Round.Time); Check(bridge.TryGetState(child, out var state), "emitted bullet has physical body");
                        bridge.TryGetDevicePose(owned.InstanceId, out var current);
                        VectorNear(current.Forward * 6, state.LinearVelocity, "physical barrel and shot heading agree");
                        Near(0, Vector3.Dot(state.LinearVelocity, current.Up), "shot remains in tilted board plane");
                        float a = Vector3.SignedAngle(down, state.LinearVelocity, current.Up); angles.Add(a); Check(Math.Abs(a) <= 22.51f, "physical fan stays in 45 degrees");
                        Near(1, simulation.GetMarbleAttribute(child, GameAttribute.BALL_BASE_VALUE), "physical bullet has one value");
                    }
                }
                Near(12, times.Count, "12.5 yields twelve actual bullets"); Check(!c.Marbles.ContainsKey(key), "fractional remainder and source are removed after final shot");
                for (int i = 1; i < times.Count; i++) Check(Math.Abs(times[i] - times[i - 1] - .1) < dt + .00001, "physical cadence is one shot every 0.1 seconds");
                Check(angles.Min() < -18 && angles.Max() > 18, "physical muzzle sweeps to both sides"); Near(19, simulation.GetOwnedDevices().Single().RemainingDurability, "complete physical batch costs one use");
                var returning = c.Marbles.Keys.First(); foreach (var k in c.Marbles.Keys.Where(k => !k.Equals(returning)).ToArray()) ContentSpawnSystem.Rollback(c, c.Marbles[k]);
                Check(bridge.TryMove(returning, spawn, down * 3), "return descendant to entrance");
                for (int n = 0; n < 100; n++) { Step(); bridge.TryGetState(returning, out var state); if (Vector3.Dot(state.Position - pose.Position, down) > .8f) break; }
                Check(bridge.TryGetState(returning, out var output) && Vector3.Dot(output.Position - pose.Position, down) > .8f, "own descendant crosses the entire device without being caught");
                Near(1, c.Marbles.Count, "returning descendant emits no extra bullets");
            }
            finally { if (obstacle != null) UnityEngine.Object.DestroyImmediate(obstacle); view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
