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
    public static class AccelerationFieldValidation
    {
        private static int assertions;
        public static void Validate()
        {
            assertions = 0;
            ConstantForce();
            RegionAndApi();
            EntryExitAndLifecycle();
            Durability();
            Debug.Log("ACCELERATION_FIELD_VALIDATION_PASSED: " + assertions + " assertions; packaged Lua, constant force, mass, substeps, oriented box and lifecycle.");
        }

        private static void ConstantForce()
        {
            foreach (float mass in new[] { 1f, 2f })
                foreach (float step in new[] { .02f, .1f })
                    using (var r = new Rig(step))
                    {
                        var key = r.Launch(new Vector3(0, .14f, .25f), new Vector3(.1f, 0, -1.5f), mass);
                        var initial = r.Body(key).velocity;
                        var spin = new Vector3(1, 2, 3); r.Body(key).angularVelocity = spin;
                        int count = Mathf.RoundToInt(.2f / step);
                        for (int i = 0; i < count; i++)
                        {
                            r.Step();
                            VectorNear(initial + r.Forward * (3f * step * (i + 1) / mass), r.Body(key).velocity,
                                "constant F produces delta-v = F * elapsed / mass, including multiple PhysX substeps");
                            VectorNear(r.Body(key).velocity, r.C.Manager.GetComponentData<Motion3D>(r.Ball(key)).LinearVelocity, "force readback synchronizes ECS");
                        }
                        VectorNear(spin, r.Body(key).angularVelocity, "center-applied force introduces no torque");
                        Near(7, r.C.Manager.GetComponentData<MarbleRush>(r.Ball(key)).Contribution, "continuous force contributes once per stay rather than each physics substep");
                        Check(!r.C.Manager.GetComponentData<BallFeatures>(r.Ball(key)).Magnetic, "ordinary non-magnetic balls are affected");
                        Check(r.C.Manager.GetBuffer<DeviceVisit>(r.Ball(key)).Length == 0, "continuous field consumes no contact visits or cooldown");
                    }
            using (var r = new Rig())
            {
                r.AddDevice(); // Independent package instance occupying the same test region.
                var key = r.Launch(new Vector3(0, .14f, 0), Vector3.zero, 1);
                r.Step(); VectorNear(r.Forward * (6 * r.StepSeconds), r.Body(key).velocity, "overlapping fields add forces");
                r.Simulation.ApplyAttribute(key, 984, GameAttribute.BALL_MAX_SPEED, AttributeOperation.Set, 1);
                Check(r.Bridge.TrySetVelocity(key, r.Forward), "set initial velocity at the configured speed cap");
                r.Step(); Near(1, r.Body(key).velocity.magnitude, "field retains the gameplay speed cap");
            }
            using (var r = new Rig())
            {
                r.SetStrength(500);
                var key = r.Launch(new Vector3(0, .14f, 0), Vector3.zero, 1);
                r.Step(); VectorNear(r.Forward * (100 * r.StepSeconds), r.Body(key).velocity, "shared field-force cap remains 100 newtons");
            }
        }

        private static void RegionAndApi()
        {
            using (var r = new Rig())
            {
                var key = r.Launch(new Vector3(0, .14f, 0), Vector3.zero, 1);
                var points = new[] { new Vector3(0, .14f, 0), new Vector3(.25f, .14f, 0), new Vector3(.29f, .14f, 0),
                    new Vector3(0, .14f, .65f), new Vector3(0, .14f, .69f), new Vector3(0, .35f, 0),
                    new Vector3(0, -.07f, 0), new Vector3(.26f, .14f, .66f), new Vector3(.25f, .14f, .65f) };
                var expected = new[] { true, true, false, true, false, false, false, false, true };
                for (int i = 0; i < points.Length; i++)
                {
                    r.Move(key, points[i], Vector3.zero);
                    var api = r.Api(key);
                    Check(api.OverlapsBox("Field") == expected[i], "sphere/rotated-box intersection including rounded corner " + i);
                    Check(!api.OverlapsBox("Missing") && !api.OverlapsBox(null) && !api.OverlapsBox(""), "missing box paths are false");
                    api.Close();
                }
                r.Move(key, new Vector3(0, .14f, 0), Vector3.zero);
                var a = r.Api(key);
                VectorNear(r.Forward, a.Forward, "forward follows local minus-Z on the tilted board");
                VectorNear(r.Board.transform.up, a.Up, "field exposes world up axis");
                Near(r.StepSeconds, a.DeltaTime, "field delta is the physics substep duration");
                Check(a.TryAddForce(Vector3.right * 2) && a.TryAddForce(Vector3.up * 3), "arbitrary world force vectors are reusable");
                VectorNear(new Vector3(2, 3, 0), a.Force, "independent commands sum within one callback");
                Check(!a.TryAddForce(new Vector3(float.NaN, 0, 0)) && !a.TryAddForce(Vector3.one * float.MaxValue), "nonfinite or overflowing force is rejected");
                VectorNear(new Vector3(2, 3, 0), a.Force, "invalid force leaves the accumulator unchanged");
                VectorNear(Vector3.zero, r.Body(key).velocity, "queueing a force does not overwrite velocity immediately");
                r.Box.enabled = false; Check(!a.OverlapsBox("Field"), "disabled box is not a region"); r.Box.enabled = true;
                a.Close();
                Expired(() => a.TryAddForce(Vector3.one)); Expired(() => a.AddRadialForce(1));
                Expired(() => a.OverlapsBox("Field")); Expired(() => { _ = a.Forward; });
                Expired(() => { _ = a.Strength; }); Expired(() => { _ = a.Magnetic; });

                // Resizing and moving the authored region requires no Lua range constants.
                r.Box.transform.localScale = new Vector3(.8f, .24f, 1.6f); r.Box.center = new Vector3(.1f, 0, 0);
                Physics.SyncTransforms(); r.Move(key, new Vector3(.45f, .14f, 0), Vector3.zero);
                var resized = r.Api(key); Check(resized.OverlapsBox("Field"), "box size and center changes are read from the collider"); resized.Close();
                r.Step(); VectorNear(r.Forward * (3 * r.StepSeconds), r.Body(key).velocity, "packaged Lua follows resized region");
            }
        }

        private static void EntryExitAndLifecycle()
        {
            using (var r = new Rig())
            {
                var key = r.Launch(new Vector3(0, .14f, .9f), Vector3.back * 1.5f, 1);
                var initial = r.Body(key).velocity;
                bool entered = false, exited = false;
                for (int i = 0; i < 90; i++)
                {
                    r.Step();
                    float along = Vector3.Dot(r.Body(key).velocity, r.Forward);
                    var local = r.View.Root.transform.InverseTransformPoint(r.Body(key).position);
                    if (i == 0) VectorNear(initial, r.Body(key).velocity, "before entering the region the ball coasts");
                    if (along > 1.501f) entered = true;
                    if (entered && local.z < -.8f)
                    {
                        var exitVelocity = r.Body(key).velocity;
                        for (int j = 0; j < 8; j++) r.Step();
                        VectorNear(exitVelocity, r.Body(key).velocity, "after leaving the box no residual force remains");
                        exited = true; break;
                    }
                }
                Check(entered && exited, "actual physics crosses and exits the rectangular field");
                r.Move(key, new Vector3(0, .14f, 0), Vector3.zero); r.Step();
                VectorNear(r.Forward * (3 * r.StepSeconds), r.Body(key).velocity, "re-entry applies continuous force again");

                r.Box.enabled = false;
                var velocity = r.Body(key).velocity; r.Step();
                VectorNear(velocity, r.Body(key).velocity, "disabling the authored region stops force");
                r.Box.enabled = true; r.View.Root.SetActive(false); r.Step();
                VectorNear(velocity, r.Body(key).velocity, "removing the device view stops force");
                r.View.Root.SetActive(true); Physics.SyncTransforms();
                r.Move(key, new Vector3(0, .14f, 0), Vector3.zero);
                var contact = new DeviceContactContext(r.C, r.Ball(key), r.Device, r.DeviceBody.Contact(key, r.Body(key).position), r.Pose);
                Check(contact.Motion.TryHold(.1f), "generic hold can suspend a ball inside the field"); contact.Close();
                var heldPosition = r.Body(key).position;
                r.Step(); r.Step();
                VectorNear(heldPosition, r.Body(key).position, "held ball ignores continuous fields");
                Check(r.Body(key).isKinematic, "field does not cancel holding");
                r.Simulation.StartSession(r.Bridge);
                Check(r.C.Marbles.Count == 0 && r.Bridge.ActiveBodyCount == 0, "reset clears the suspended body and field interactions");
            }
        }

        private static void Durability()
        {
            foreach (int budget in new[] { 20, -1 })
            using (var r = new Rig())
            {
                r.C.Balance.Devices.Single(d => d.Id == 9004).Durability = budget;
                var key = r.Launch(new Vector3(0, .14f, 0), Vector3.zero, 1);
                int entries = budget == -1 ? 25 : 20;
                for (int i = 0; i < entries; i++)
                {
                    r.Move(key, new Vector3(0, .14f, 2), Vector3.zero); r.Step();
                    r.Move(key, new Vector3(0, .14f, 0), Vector3.zero); r.Step();
                    Near(budget == -1 ? -1 : 19 - i, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "each re-entry spends one use; infinite budget stays infinite");
                    VectorNear(r.Forward * (3 * r.StepSeconds), r.Body(key).velocity, "last accepted entry still receives its force");
                    if (i == 0)
                    {
                        for (int j = 0; j < 3; j++) r.Step();
                        Near(budget == -1 ? -1 : 19, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "continuous overlap does not drain per step");
                    }
                }
                if (budget == -1) Check(r.Box.enabled, "unlimited field is still active after 25 entries");
                else
                {
                    Check(!r.Box.enabled, "last entry disables the field region");
                    var velocity = r.Body(key).velocity; r.Step(); VectorNear(velocity, r.Body(key).velocity, "spent field stops adding force");
                    var round = r.C.Round; round.Phase = (byte)RoundPhase.Build; r.C.Round = round; r.Simulation.BeginRound();
                    Check(r.Box.enabled, "next round restores the field collider");
                    Near(20, r.Simulation.GetOwnedDevices().Single().RemainingDurability, "field durability resets each round");
                }
            }
        }

        private static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception("Acceleration field: " + message); }
        private static void Near(double expected, double actual, string message) => Check(Math.Abs(expected - actual) < .0002, message + ": " + expected + " vs " + actual);
        private static void VectorNear(Vector3 expected, Vector3 actual, string message) => Check((expected - actual).magnitude < .0004f, message + ": " + expected + " vs " + actual);
        private static void Expired(Action action)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "field API cannot be retained after callback"); }

        private sealed class Rig : IDisposable
        {
            internal readonly float StepSeconds;
            internal readonly Scene Scene;
            internal readonly GameObject Board;
            internal readonly MarbleSimulation Simulation;
            internal readonly MarblePhysicsBridge Bridge;
            internal readonly Entity Device;
            internal readonly MResLoadHandle View;
            internal readonly OwnedDeviceSnapshot Owned;
            private readonly List<MResLoadHandle> views = new List<MResLoadHandle>();
            internal SimulationContext C => Simulation.Context;
            internal MResDeviceBody DeviceBody => View.Root.GetComponent<MResDeviceBody>();
            internal BoxCollider Box => View.Root.transform.Find("Field").GetComponent<BoxCollider>();
            internal Vector3 Forward => View.Root.transform.rotation * Vector3.back;
            internal DevicePose Pose { get { Bridge.TryGetDevicePose(Owned.InstanceId, out var pose); return pose; } }
            internal Rig(float step = .02f)
            {
                StepSeconds = step;
                Scene = SceneManager.CreateScene("AccelerationFieldValidation_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var balance = GameBalanceLoader.Load();
                foreach (var d in balance.Devices) d.StartingCount = 0;
                balance.Marble.InitialBlood = balance.Marble.BloodCapacity = 1000; balance.Marble.FireIntervalSeconds = .01f;
                balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue = 60;
                Simulation = new MarbleSimulation(balance); Bridge = new MarblePhysicsBridge(Scene, null, null, Simulation.EnqueueContact);
                Simulation.StartSession(Bridge); Simulation.BeginRound();
                Simulation.ApplyGlobalAttribute(982, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                Board = new GameObject("ForceFieldBoard"); SceneManager.MoveGameObjectToScene(Board, Scene);
                Board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 47, 19));
                View = AddDevice(); Owned = Simulation.GetOwnedDevices().Single(); CampaignStateUtility.FindDevice(C, Owned.InstanceId, out Device);
                Check(Box.isTrigger && Box.enabled, "package restores a non-solid box field");
                VectorNear(new Vector3(.4f, .24f, 1.2f), Box.transform.localScale, "default region has the requested dimensions");
            }
            internal MResLoadHandle AddDevice()
            {
                var definition = C.Balance.Devices.Single(d => d.Id == 9004);
                CampaignStateUtility.CreateDevice(C, definition, definition.Price);
                var owned = Simulation.GetOwnedDevices().OrderBy(d => d.InstanceId).Last();
                CampaignStateUtility.FindDevice(C, owned.InstanceId, out var entity);
                var data = C.Manager.GetComponentData<OwnedDeviceData>(entity); data.Placed = true; C.Manager.SetComponentData(entity, data);
                var view = MResDeviceView.Create(owned, Board.transform, C.DeviceScripts.Get(owned.InstanceId, definition.Id));
                views.Add(view); view.Root.SetActive(true); Physics.SyncTransforms(); return view;
            }
            internal void SetStrength(float strength)
            {
                var definition = C.Balance.Devices.Single(d => d.Id == 9004).Copy(); definition.Strength = strength;
                DeviceBody.Initialize(Owned.InstanceId, definition, C.DeviceScripts.Get(Owned.InstanceId, definition.Id));
            }
            internal Entity Ball(MarbleKey key) => C.Marbles[key];
            internal MarbleBody Marble(MarbleKey key) => Scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key));
            internal Rigidbody Body(MarbleKey key) => Marble(key).Body;
            internal DeviceFieldContext Api(MarbleKey key) => new DeviceFieldContext(DeviceBody, Marble(key), StepSeconds);
            internal MarbleKey Launch(Vector3 localPosition, Vector3 localVelocity, float mass)
            {
                Simulation.FireOnce(); Simulation.BeforePhysics(StepSeconds, Bridge, new Vector3(4, 5, 0), Quaternion.identity); Simulation.AfterPhysics(Bridge);
                var key = C.Marbles.Keys.OrderBy(k => k.Sequence).Last();
                Body(key).drag = Body(key).angularDrag = 0;
                Simulation.ApplyAttribute(key, 983, GameAttribute.BALL_MASS, AttributeOperation.Set, mass);
                Move(key, localPosition, localVelocity); return key;
            }
            internal void Move(MarbleKey key, Vector3 localPosition, Vector3 localVelocity) =>
                Check(Bridge.TryMove(key, View.Root.transform.TransformPoint(localPosition), View.Root.transform.rotation * localVelocity), "fixture moves without solid obstacles");
            internal void Step()
            { Simulation.BeforePhysics(StepSeconds, Bridge, Vector3.up * 10, Quaternion.identity); Bridge.Simulate(StepSeconds); Simulation.AfterPhysics(Bridge); }
            public void Dispose()
            {
                foreach (var view in views) view.Dispose();
                Simulation.Dispose(); Bridge.Dispose(); UnityEngine.Object.DestroyImmediate(Board); SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }
}
