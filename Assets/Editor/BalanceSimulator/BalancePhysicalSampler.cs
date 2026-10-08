using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MarblesECS.PhysX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    /// <summary>
    /// Samples a temporary clone of the authored board with the same MarblePhysicsBridge used by gameplay.
    /// The authored root is disabled while Unity Physics is advanced manually, then its state is restored.
    /// It intentionally does not apply economy or device effects. Those are evaluated by the Monte Carlo layer.
    /// </summary>
    internal static class BalancePhysicalSampler
    {
        internal const string DefaultScene = "Assets/Scenes/TestScene.unity";
        internal const string DefaultJson = "Docs/Balance/physical_profile.json";
        internal const string DefaultCsv = "Docs/Balance/physical_samples.csv";
        private static readonly float[] Flows = { 0f, .5f, 1f };
        private static readonly string[] Lanes = { "Left", "Center", "Right" };
        private static readonly float[] DeviceRadii = { .04f, .12f, .18f };
        private const float DensityGrid = .1f;

        [Serializable]
        internal sealed class Report
        {
            public int SchemaVersion = 1;
            public string Scene;
            public string UnityVersion;
            public string GeneratedUtc;
            public int Seed;
            public int SampleCount;
            public float FixedStep;
            public string PhysicsFingerprint;
            public Profile[] Profiles;
            public string[] Limitations;
        }

        [Serializable]
        internal sealed class Profile
        {
            public string Lane;
            public float Flow;
            public int Samples;
            public Outcome[] Outcomes;
            public float MeanFlightSeconds;
            public float MeanPinContacts;
            public float MeanDeviceContacts;
            public Hotspot[] Hotspots;
        }

        [Serializable]
        internal sealed class Outcome
        {
            public string Kind;
            public int TargetId;
            public float Multiplier;
            public float BaseRushChance;
            public bool RushEnabled;
            public int Count;
            public float Probability;
            public float MeanFlightSeconds;
        }

        [Serializable]
        internal sealed class Hotspot
        {
            public float DeviceRadius;
            public float HitProbability;
            public float BoardX;
            public float BoardZ;
            public string Provenance = "EMPIRICAL_BEST_OBSERVED_CELL";
        }

        private sealed class RawSample
        {
            internal int Index;
            internal int Seed;
            internal string Lane;
            internal float Flow;
            internal float LaunchX;
            internal string Kind;
            internal int TargetId;
            internal float Multiplier;
            internal float BaseRushChance;
            internal bool RushEnabled;
            internal float FlightSeconds;
            internal int PinContacts;
            internal int DeviceContacts;
        }

        private sealed class Aggregate
        {
            internal readonly List<RawSample> Samples = new List<RawSample>();
            internal readonly Dictionary<string, List<RawSample>> Outcomes = new Dictionary<string, List<RawSample>>();
            internal readonly Dictionary<float, Dictionary<long, int>> Density = DeviceRadii.ToDictionary(
                radius => radius, radius => new Dictionary<long, int>());
        }

        internal static Report Run(string scenePath, int sampleCount, int seed, string jsonPath, string csvPath)
        {
            if (sampleCount < 90) throw new ArgumentOutOfRangeException(nameof(sampleCount), "Use at least 90 samples.");
            if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath)) throw new FileNotFoundException("Balance scene not found.", scenePath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? ".");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? ".");

            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Physical sampling cancelled before changing the editor scene setup.");
            Scene authored = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var controller = authored.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MarbleGameController>(true))
                .FirstOrDefault();
            if (controller == null || controller.PhysicsRoot == null || controller.LaunchPoint == null)
                throw new InvalidOperationException("The sampling scene needs a configured MarbleGameController, PhysicsRoot and LaunchPoint.");
            GameBalance balance = GameBalanceLoader.Load(controller.BalanceJson);

            Scene simulationScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject physicsClone = null;
            MarblePhysicsBridge bridge = null;
            bool originalPhysicsRootActive = controller.PhysicsRoot.gameObject.activeSelf;
            SimulationMode previousSimulationMode = Physics.simulationMode;
            var rows = new List<RawSample>(sampleCount);
            try
            {
                physicsClone = UnityEngine.Object.Instantiate(controller.PhysicsRoot.gameObject);
                physicsClone.name = controller.PhysicsRoot.name + " [Balance Sampling]";
                SceneManager.MoveGameObjectToScene(physicsClone, simulationScene);
                controller.PhysicsRoot.gameObject.SetActive(false);
                Physics.simulationMode = SimulationMode.Script;
                Transform cloneRoot = physicsClone.transform;
                Transform cloneLaunch = FindEquivalent(controller.PhysicsRoot, cloneRoot, controller.LaunchPoint);
                if (cloneLaunch == null) throw new InvalidOperationException("LaunchPoint must be inside PhysicsRoot for physical sampling.");

                bridge = new MarblePhysicsBridge(simulationScene, controller.MarblePrefab, controller.MarbleMaterial,
                    ReceiveContact);
                var aggregates = new Dictionary<string, Aggregate>();
                int profileCount = Lanes.Length * Flows.Length;
                int perProfile = sampleCount / profileCount;
                int remainder = sampleCount % profileCount;
                int index = 0;
                Vector3 originalLocal = cloneLaunch.localPosition;
                for (int laneIndex = 0; laneIndex < Lanes.Length; laneIndex++)
                for (int flowIndex = 0; flowIndex < Flows.Length; flowIndex++)
                {
                    string lane = Lanes[laneIndex];
                    float flow = Flows[flowIndex];
                    string profileKey = Key(lane, flow);
                    var aggregate = new Aggregate();
                    aggregates.Add(profileKey, aggregate);
                    int count = perProfile + (remainder-- > 0 ? 1 : 0);
                    for (int localIndex = 0; localIndex < count; localIndex++)
                    {
                        index++;
                        int sampleSeed = unchecked(seed + index * 7919);
                        float half = controller.LauncherMoveHalfWidth;
                        float lower = laneIndex == 0 ? -half : laneIndex == 1 ? -half / 3f : half / 3f;
                        float upper = laneIndex == 0 ? -half / 3f : laneIndex == 1 ? half / 3f : half;
                        var sampleRandom = new System.Random(sampleSeed);
                        float launchX = Mathf.Lerp(lower, upper, (float)sampleRandom.NextDouble());
                        cloneLaunch.localPosition = originalLocal + Vector3.right * launchX;
                        RawSample sample = SimulateOne(bridge, balance, controller, cloneRoot, cloneLaunch, flow,
                            lane, index, sampleSeed, aggregate);
                        rows.Add(sample);
                        aggregate.Samples.Add(sample);
                        string outcomeKey = sample.Kind + ":" + sample.TargetId.ToString(CultureInfo.InvariantCulture) + ":" +
                            sample.Multiplier.ToString("R", CultureInfo.InvariantCulture) + ":" +
                            sample.BaseRushChance.ToString("R", CultureInfo.InvariantCulture) + ":" + sample.RushEnabled;
                        if (!aggregate.Outcomes.TryGetValue(outcomeKey, out var outcomeRows))
                            aggregate.Outcomes.Add(outcomeKey, outcomeRows = new List<RawSample>());
                        outcomeRows.Add(sample);
                        if (index % 50 == 0)
                        {
                            EditorUtility.DisplayProgressBar("Pachinko physical sampling",
                                index.ToString(CultureInfo.InvariantCulture) + " / " + sampleCount.ToString(CultureInfo.InvariantCulture),
                                (float)index / sampleCount);
                        }
                    }
                }
                cloneLaunch.localPosition = originalLocal;

                var report = new Report
                {
                    Scene = scenePath.Replace('\\', '/'), UnityVersion = Application.unityVersion,
                    GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Seed = seed,
                    SampleCount = rows.Count, FixedStep = controller.FixedStep,
                    PhysicsFingerprint = AssetDatabase.GetAssetDependencyHash(scenePath).ToString(),
                    Profiles = aggregates.Select(pair => BuildProfile(pair.Key, pair.Value)).ToArray(),
                    Limitations = new[]
                    {
                        "PhysX is not cross-platform deterministic; Seed reproduces launch choices, not solver bit patterns.",
                        "Hotspot exposure is the best observed legal-path cell, not a promise that a player will place a device there.",
                        "Purchased device effects are excluded from the board sample and are applied by the economy layer.",
                        "No SpecialBall catalog exists in the current runtime; related KPIs are reported as N/A."
                    }
                };
                File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
                WriteCsv(csvPath, rows);
                AssetDatabase.Refresh();
                return report;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (bridge != null) bridge.Dispose();
                if (physicsClone != null) UnityEngine.Object.DestroyImmediate(physicsClone);
                Physics.simulationMode = previousSimulationMode;
                if (controller != null && controller.PhysicsRoot != null)
                    controller.PhysicsRoot.gameObject.SetActive(originalPhysicsRootActive);
                if (simulationScene.IsValid() && simulationScene.isLoaded)
                    EditorSceneManager.CloseScene(simulationScene, true);
                if (!Application.isBatchMode) EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                else if (authored.IsValid() && authored.isLoaded) SceneManager.SetActiveScene(authored);
            }
        }

        private static RawSample SimulateOne(MarblePhysicsBridge bridge, GameBalance balance,
            MarbleGameController controller, Transform cloneRoot, Transform launch, float flow, string lane, int index, int sampleSeed,
            Aggregate aggregate)
        {
            float investment = balance.Marble.MinInvestment + (balance.Marble.MaxInvestment - balance.Marble.MinInvestment) * flow;
            double scale = investment / balance.Marble.MinInvestment;
            float radius = (float)(balance.Content.Definition(GameAttribute.BALL_RADIUS).DefaultValue *
                Math.Pow(scale, 1d / 3d) * AttributeRuntime.RadiusToWorld);
            float mass = (float)(balance.Content.Definition(GameAttribute.BALL_MASS).DefaultValue * scale);
            float speed = (float)balance.Content.Definition(GameAttribute.BALL_LAUNCH_SPEED).DefaultValue;
            var settings = new MarblePhysicalSettings
            {
                Radius = radius,
                Mass = mass,
                Bounce = (float)balance.Content.Definition(GameAttribute.BALL_BOUNCE).DefaultValue,
                Friction = (float)balance.Content.Definition(GameAttribute.BALL_FRICTION).DefaultValue,
                Gravity = (float)balance.Content.Definition(GameAttribute.BALL_GRAVITY_MOD).DefaultValue,
                MaxSpeed = (float)balance.Content.Definition(GameAttribute.BALL_MAX_SPEED).DefaultValue,
                MagnetResponse = (float)balance.Content.Definition(GameAttribute.BALL_MAGNET_RESPONSE).DefaultValue
            };
            var key = new MarbleKey(index + 1, 1);
            Vector3 position = launch.TransformPoint(controller.LaunchOffset);
            Vector3 velocity = launch.rotation * Vector3.down * speed;
            var quote = new ShotQuote(investment, investment, radius, mass,
                balance.Content.Definition(GameAttribute.BALL_BASE_VALUE).DefaultValue * Math.Pow(scale, balance.Content.FlowValueExponent), 1);
            if (!bridge.TrySpawn(new MarbleSpawnData
                {
                    Key = key, Position = position, Rotation = launch.rotation, Quote = quote,
                    LinearVelocity = velocity, HasRestitution = true, Restitution = Mathf.Min(1, settings.Bounce),
                    HasSettings = true, Settings = settings
                }))
                throw new InvalidOperationException("Physical sampler could not spawn at launcher position " + position + ".");

            float elapsed = 0;
            int pinContacts = 0, deviceContacts = 0;
            MarbleContact? terminal = null;
            float lifetime = (float)balance.Content.Definition(GameAttribute.BALL_LIFETIME).DefaultValue;
            var visited = DeviceRadii.ToDictionary(value => value, value => new HashSet<long>());
            try
            {
                while (elapsed < lifetime)
                {
                    var contacts = new List<MarbleContact>();
                    // Bridge callbacks are synchronous; temporarily collect through the shared callback list by
                    // reading contacts reported since the previous simulation step.
                    SamplingContactBuffer.Begin(contacts);
                    bridge.Simulate(controller.FixedStep);
                    SamplingContactBuffer.End();
                    elapsed += controller.FixedStep;
                    foreach (MarbleContact contact in contacts)
                    {
                        if (contact.Kind == MarbleContactKind.Multiplier || contact.Kind == MarbleContactKind.RushPin ||
                            contact.Kind == MarbleContactKind.Pin || contact.Kind == MarbleContactKind.Pierced) pinContacts++;
                        if (contact.Kind == MarbleContactKind.Device) deviceContacts++;
                    }
                    var terminals = contacts.Where(IsTerminal).OrderByDescending(x => x.Priority).ThenBy(x => x.TargetId).ToArray();
                    if (terminals.Length > 0) { terminal = terminals[0]; break; }
                    if (!bridge.TryGetPosition(key, out Vector3 current) || !SimulationContext.IsFinite(current) ||
                        current.y < balance.Marble.DespawnBelowY || Math.Abs(current.x) > balance.Marble.WorldBoundsExtent ||
                        Math.Abs(current.y) > balance.Marble.WorldBoundsExtent || Math.Abs(current.z) > balance.Marble.WorldBoundsExtent)
                        break;
                    Vector3 local = cloneRoot.InverseTransformPoint(current);
                    foreach (float deviceRadius in DeviceRadii)
                        AddDensityCells(visited[deviceRadius], local.x, local.z, deviceRadius + radius);
                }
            }
            finally
            {
                bridge.Remove(key);
            }
            foreach (float deviceRadius in DeviceRadii)
            {
                Dictionary<long, int> density = aggregate.Density[deviceRadius];
                foreach (long cell in visited[deviceRadius])
                    density[cell] = density.TryGetValue(cell, out int count) ? count + 1 : 1;
            }
            MarbleContact resolved = terminal ?? new MarbleContact { Kind = MarbleContactKind.Drain, TargetId = 0 };
            return new RawSample
            {
                Index = index, Seed = sampleSeed, Lane = lane, Flow = flow,
                LaunchX = launch.localPosition.x, Kind = terminal.HasValue ? resolved.Kind.ToString() : "Void",
                TargetId = resolved.TargetId, Multiplier = (float)resolved.Multiplier,
                BaseRushChance = (float)resolved.BaseRushChance, RushEnabled = !resolved.RushDisabled,
                FlightSeconds = elapsed, PinContacts = pinContacts, DeviceContacts = deviceContacts
            };
        }

        // The bridge callback is created once, while each simulation step needs an isolated buffer.
        private static class SamplingContactBuffer
        {
            [ThreadStatic] private static List<MarbleContact> active;
            internal static void Begin(List<MarbleContact> target) { active = target; }
            internal static void End() { active = null; }
            internal static void Add(MarbleContact contact) { if (active != null) active.Add(contact); }
        }

        internal static void ReceiveContact(MarbleContact contact) { SamplingContactBuffer.Add(contact); }

        private static bool IsTerminal(MarbleContact contact) => contact.Kind == MarbleContactKind.Score ||
            contact.Kind == MarbleContactKind.RandomScore || contact.Kind == MarbleContactKind.Drain;

        private static void AddDensityCells(HashSet<long> cells, float x, float z, float radius)
        {
            int centerX = Mathf.RoundToInt(x / DensityGrid);
            int centerZ = Mathf.RoundToInt(z / DensityGrid);
            int range = Mathf.CeilToInt(radius / DensityGrid);
            for (int dx = -range; dx <= range; dx++)
            for (int dz = -range; dz <= range; dz++)
            {
                float worldX = dx * DensityGrid;
                float worldZ = dz * DensityGrid;
                if (worldX * worldX + worldZ * worldZ > radius * radius) continue;
                cells.Add(Pack(centerX + dx, centerZ + dz));
            }
        }

        private static Profile BuildProfile(string key, Aggregate aggregate)
        {
            string[] parts = key.Split('|');
            int samples = aggregate.Samples.Count;
            return new Profile
            {
                Lane = parts[0], Flow = float.Parse(parts[1], CultureInfo.InvariantCulture), Samples = samples,
                Outcomes = aggregate.Outcomes.Values.Select(group =>
                {
                    RawSample first = group[0];
                    return new Outcome
                    {
                        Kind = first.Kind, TargetId = first.TargetId, Multiplier = first.Multiplier,
                        BaseRushChance = first.BaseRushChance, RushEnabled = first.RushEnabled,
                        Count = group.Count, Probability = samples == 0 ? 0 : (float)group.Count / samples,
                        MeanFlightSeconds = group.Count == 0 ? 0 : group.Average(x => x.FlightSeconds)
                    };
                }).OrderBy(x => x.Kind).ThenBy(x => x.TargetId).ToArray(),
                MeanFlightSeconds = samples == 0 ? 0 : aggregate.Samples.Average(x => x.FlightSeconds),
                MeanPinContacts = samples == 0 ? 0 : (float)aggregate.Samples.Average(x => x.PinContacts),
                MeanDeviceContacts = samples == 0 ? 0 : (float)aggregate.Samples.Average(x => x.DeviceContacts),
                Hotspots = DeviceRadii.Select(radius =>
                {
                    var best = aggregate.Density[radius].OrderByDescending(x => x.Value).FirstOrDefault();
                    Unpack(best.Key, out int x, out int z);
                    return new Hotspot { DeviceRadius = radius, HitProbability = samples == 0 ? 0 : (float)best.Value / samples,
                        BoardX = x * DensityGrid, BoardZ = z * DensityGrid };
                }).ToArray()
            };
        }

        private static string Key(string lane, float flow) => lane + "|" + flow.ToString("R", CultureInfo.InvariantCulture);
        private static long Pack(int x, int z) => ((long)x << 32) ^ (uint)z;
        private static void Unpack(long packed, out int x, out int z) { x = (int)(packed >> 32); z = (int)packed; }

        private static Transform FindEquivalent(Transform sourceRoot, Transform cloneRoot, Transform source)
        {
            var names = new Stack<string>();
            Transform cursor = source;
            while (cursor != null && cursor != sourceRoot) { names.Push(cursor.name); cursor = cursor.parent; }
            if (cursor != sourceRoot) return null;
            Transform result = cloneRoot;
            while (names.Count > 0) { result = result.Find(names.Pop()); if (result == null) return null; }
            return result;
        }

        private static void WriteCsv(string path, IEnumerable<RawSample> rows)
        {
            var output = new StringBuilder();
            output.AppendLine("sample_index,sample_seed,lane,flow,launch_x,outcome_kind,target_id,zone_multiplier,base_rush_chance,rush_enabled,flight_seconds,pin_contacts,device_contacts");
            foreach (RawSample row in rows)
            {
                output.Append(row.Index).Append(',').Append(row.Seed).Append(',').Append(row.Lane).Append(',')
                    .Append(row.Flow.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.LaunchX.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(row.Kind).Append(',')
                    .Append(row.TargetId).Append(',').Append(row.Multiplier.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.BaseRushChance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.RushEnabled ? "true" : "false").Append(',')
                    .Append(row.FlightSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.PinContacts).Append(',').Append(row.DeviceContacts).AppendLine();
            }
            File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
        }
    }
}
