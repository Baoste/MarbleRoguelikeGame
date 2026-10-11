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
        private static void ReflectorLuaApi()
        {
            int start = assertions;
            using (var f = new Fixture())
            {
                var definition = f.C.Balance.Devices.Single(d => d.Id == 9002);
                definition.Durability = -1; // Angle sampling exercises unlimited usage; finite wear has separate coverage.
                var device = f.Add(definition); var key = f.Launch(); var ball = f.Ball(key);
                f.Simulation.ApplyAttribute(key, 985, GameAttribute.BALL_LIFETIME, AttributeOperation.Set, 60);
                var rotation = Quaternion.Euler(61, 123, 17);
                var up = rotation * Vector3.up; var normal = rotation * Vector3.back;
                var pose = f.Physics.Poses[device.InstanceId]; pose.Up = up; pose.Forward = rotation * Vector3.back;
                f.Physics.Poses[device.InstanceId] = pose;
                var angles = new List<float>();
                foreach (float speed in new[] { .000005f, .03f, 5f })
                foreach (float incidence in new[] { -80f, -35f, 0f, 35f, 80f })
                for (int i = 0; i < 4; i++)
                {
                    var incoming = Quaternion.AngleAxis(incidence, up) * -normal * speed;
                    var hit = f.Hit(key, device); hit.HasImpact = true; hit.Normal = normal * 3; hit.IncomingVelocity = incoming;
                    var state = f.Physics.States[key]; state.LinearVelocity = up * 9; state.AngularVelocity = new Vector3(1, 2, 3);
                    f.Physics.States[key] = state;
                    f.Step(hit);
                    var actual = f.Physics.States[key];
                    var reflected = Vector3.Reflect(incoming / speed, normal);
                    float angle = Vector3.SignedAngle(reflected, actual.LinearVelocity / speed, up); angles.Add(angle);
                    Check(Math.Abs(actual.LinearVelocity.magnitude - speed) < Math.Max(1e-8f, speed * .0001f), "reflection preserves the pre-solver speed, including very small speeds");
                    Check(Math.Abs(angle) <= 30.001f, "Lua random reflection remains within the configured half-angle");
                    Check(Vector3.Dot(actual.LinearVelocity / speed, normal) >= -.00001f, "grazing reflection leaves the surface");
                    Check(Math.Abs(Vector3.Dot(actual.LinearVelocity / speed, up)) < .00001f, "reflection stays in the rotated board plane");
                    VectorNear(state.Position, actual.Position, "reflection changes velocity without teleporting");
                    VectorNear(state.AngularVelocity, actual.AngularVelocity, "reflection API leaves spin unchanged");
                    VectorNear(actual.LinearVelocity, f.C.Manager.GetComponentData<Motion3D>(ball).LinearVelocity, "reflection synchronizes ECS");
                }
                Check(angles.Any(a => a < -5) && angles.Any(a => a > 5) && angles.Distinct().Count() > 30, "packaged Lua samples varied offsets on both sides");

                definition.Value = 0;
                var straight = f.Hit(key, device); straight.HasImpact = true; straight.Normal = normal; straight.IncomingVelocity = -normal * 5;
                uint seed = f.C.Round.RandomState; f.Step(straight);
                VectorNear(normal * 5, f.Physics.States[key].LinearVelocity, "zero spread gives exact mirror reflection");
                Check(f.C.Round.RandomState == seed, "zero-width random range does not advance the RNG");
                definition.Value = 30;
                f.Simulation.ApplyAttribute(key, 986, GameAttribute.BALL_MAX_SPEED, AttributeOperation.Set, 2);
                f.Step(straight); Near(2, f.Physics.States[key].LinearVelocity.magnitude, "reflection still respects the current speed cap");

                var before = f.Physics.States[key]; seed = f.C.Round.RandomState;
                f.Step(f.Hit(key, device));
                VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "contacts without impact geometry do not trigger random reflection");
                var invalid = straight; invalid.Normal = Vector3.zero; f.Step(invalid);
                invalid.Normal = new Vector3(float.NaN, 0, 0); f.Step(invalid);
                invalid.Normal = normal; invalid.IncomingVelocity = new Vector3(float.PositiveInfinity, 0, 0); f.Step(invalid);
                invalid.IncomingVelocity = normal; f.Step(invalid); // already separating
                invalid.IncomingVelocity = Vector3.zero; f.Step(invalid);
                invalid.IncomingVelocity = -normal; invalid.Normal = up; f.Step(invalid); // cylinder top
                VectorNear(before.LinearVelocity, f.Physics.States[key].LinearVelocity, "invalid, stationary, separating and top contacts leave ordinary physics alone");
                Check(seed == f.C.Round.RandomState, "ignored contacts do not consume random state");

                CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var entity);
                var api = new DeviceContactContext(f.C, ball, entity, straight, pose);
                var impact = api.Impact; var vectors = api.Vectors;
                VectorNear(straight.IncomingVelocity, impact.IncomingVelocity, "impact exposes the immutable entry velocity");
                VectorNear(normal, impact.Normal, "impact exposes a unit normal");
                VectorNear(straight.Position, impact.Position, "impact exposes the actual contact point");
                VectorNear(Vector3.right, vectors.Normalize(Vector3.right * .000005f), "vector normalization supports low-speed directions");
                VectorNear(Vector3.zero, vectors.Normalize(Vector3.zero), "zero normalization is defined");
                VectorNear(Vector3.right, vectors.ProjectOnPlane(Vector3.one, new Vector3(0, 1, 1)), "plane projection is independent of motion");
                VectorNear(new Vector3(1, 1, 0), vectors.Reflect(new Vector3(1, -1, 0), Vector3.up * 2), "reflection normalizes its normal");
                Near(90, vectors.SignedAngle(Vector3.forward, Vector3.right, Vector3.up), "signed rotation uses degrees");
                Near(14, vectors.Dot(new Vector3(1, 2, 3), new Vector3(1, 2, 3)), "vector dot product");
                uint expectedState = 456;
                var round = f.C.Round; round.RandomState = expectedState; f.C.Round = round;
                for (int i = 0; i < 32; i++) Near(-30 + 60 * MarbleRules.NextRandom(ref expectedState), api.RandomRange(-30, 30), "range follows the seeded gameplay RNG");
                Check(expectedState == f.C.Round.RandomState, "random API advances only the round RNG");
                ExpectArgument(() => api.RandomRange(2, 1)); ExpectArgument(() => api.RandomRange(double.NaN, 1));
                ExpectArgument(() => api.RandomRange(-double.MaxValue, double.MaxValue));
                ExpectArgument(() => vectors.Reflect(Vector3.one, Vector3.zero));
                ExpectArgument(() => vectors.SignedAngle(Vector3.zero, Vector3.one, Vector3.up));
                ExpectArgument(() => vectors.Normalize(new Vector3(float.PositiveInfinity, 0, 0)));
                api.Close();
                ExpectExpired(() => { _ = impact.IncomingSpeed; });
                ExpectExpired(() => { vectors.Reflect(Vector3.forward, Vector3.back); });
                ExpectExpired(() => { api.RandomRange(0, 1); });
                ExpectExpired(() => { _ = api.Impact; });
            }
            ReflectorWithPhysX(0);
            ReflectorWithPhysX(.12f);
            Debug.Log("REFLECTOR_VALIDATION_PASSED: " + (assertions - start) + " assertions; packaged Lua, impact snapshot, angle bounds, seeded randomness and real cylinder collisions.");
        }

        private static void ExpectArgument(Action action)
        { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, "invalid vector/random input rejected"); }
        private static void ExpectExpired(Action action)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "impact/vector/random API expires at callback end"); }

        private static void ReflectorWithPhysX(float offset)
        {
            var scene = SceneManager.CreateScene("ReflectorPhysics_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var balance = Balance();
            var simulation = new MarbleSimulation(balance);
            var hits = new List<MarbleContact>();
            var bridge = new MarblePhysicsBridge(scene, null, null, contact => { if (contact.HasImpact) hits.Add(contact); simulation.EnqueueContact(contact); });
            var board = new GameObject("ReflectorBoard"); SceneManager.MoveGameObjectToScene(board, scene);
            MResLoadHandle view = null;
            try
            {
                simulation.StartSession(bridge); simulation.BeginRound(); var c = simulation.Context;
                simulation.ApplyGlobalAttribute(987, GameAttribute.BALL_GRAVITY_MOD, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(988, GameAttribute.BALL_BOUNCE, AttributeOperation.Set, 0);
                simulation.ApplyGlobalAttribute(989, GameAttribute.BALL_FRICTION, AttributeOperation.Set, 0);
                var definition = c.Balance.Devices.Single(d => d.Id == 9002);
                CampaignStateUtility.CreateDevice(c, definition, definition.Price);
                var owned = simulation.GetOwnedDevices().Single();
                CampaignStateUtility.FindDevice(c, owned.InstanceId, out var device);
                var data = c.Manager.GetComponentData<OwnedDeviceData>(device); data.Placed = true; c.Manager.SetComponentData(device, data);
                board.transform.SetPositionAndRotation(new Vector3(0, 3, 0), Quaternion.Euler(60, 127, 0));
                view = MResDeviceView.Create(owned, board.transform, c.DeviceScripts.Get(owned.InstanceId, definition.Id));
                view.Root.SetActive(true); Physics.SyncTransforms();
                var collider = view.Root.GetComponentInChildren<MeshCollider>();
                Check(collider != null && collider.convex && !collider.isTrigger, "package contains a solid cylindrical collider");
                var spawn = board.transform.TransformPoint(new Vector3(offset, .1f, .8f));
                simulation.FireOnce(); simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); simulation.AfterPhysics(bridge);
                var key = c.Marbles.Keys.Single();
                var body = scene.GetRootGameObjects().Select(go => go.GetComponent<MarbleBody>()).Single(b => b != null && b.Key.Equals(key)).Body;
                body.drag = body.angularDrag = 0;
                var incoming = board.transform.rotation * Vector3.back * 2;
                bridge.TrySetVelocity(key, incoming);
                int steps = 0;
                while (hits.Count == 0 && steps++ < 40)
                { simulation.BeforePhysics(.02f, bridge, spawn, Quaternion.identity); bridge.Simulate(.02f); simulation.AfterPhysics(bridge); }
                Check(hits.Count > 0, "real PhysX collision supplies an impact snapshot");
                var hit = hits[0]; var up = board.transform.up;
                VectorNear(incoming, hit.IncomingVelocity, "snapshot retains entry velocity even with zero natural restitution");
                var normal = Vector3.ProjectOnPlane(hit.Normal, up).normalized;
                var reflected = Vector3.Reflect(incoming, normal).normalized;
                Near(2, body.velocity.magnitude, "Lua restores entry speed rather than the post-solver speed");
                Check(Math.Abs(Vector3.SignedAngle(reflected, body.velocity.normalized, up)) <= 30.01f, "physical reflection respects the 30 degree range");
                Check(Vector3.Dot(body.velocity, normal) >= -.0001f, "physical reflection exits the cylinder surface");
                Check(Math.Abs(Vector3.Dot(body.velocity, up)) < .0001f, "physical reflection follows the tilted board plane");
                VectorNear(body.velocity, c.Manager.GetComponentData<Motion3D>(c.Marbles[key]).LinearVelocity, "real reflection matches ECS velocity");
                VectorNear(body.angularVelocity, c.Manager.GetComponentData<Motion3D>(c.Marbles[key]).AngularVelocity, "reflection keeps physics spin and ECS in sync");
                var visits = c.Manager.GetBuffer<DeviceVisit>(c.Marbles[key]);
                int entries = 0, count = 0;
                for (int i = 0; i < visits.Length; i++)
                    if (visits[i].Key == 100000 + owned.InstanceId) { entries++; count += visits[i].Count; }
                Check(entries == 1 && count == 1, "multiple substeps do not duplicate the same impact trigger");
            }
            finally
            {
                view?.Dispose(); simulation.Dispose(); bridge.Dispose(); UnityEngine.Object.DestroyImmediate(board); SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
