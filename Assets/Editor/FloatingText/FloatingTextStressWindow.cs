using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MarblesECS.Editor
{
    internal sealed class FloatingTextStressWindow : EditorWindow
    {
        private static readonly int[] Sizes = { 100, 500, 1000, 2000 };
        private Camera selectedCamera, view;
        private GameObject root;
        private FloatingTextSystem system;
        private int capacity = 2000, eventsPerSecond = 6000, sequence, lastFrame;
        private bool continuous, frozen, merge = true;
        private double lastTime, animationTime, fraction, sampleTime;
        private long received, spawned, merged, dropped;
        private double receivedRate, spawnedRate, mergedRate, droppedRate, updateMs;
        private long allocatedBytes;
        private ProfilerRecorder upload, draws, batches, frameGc;

        [MenuItem("Marbles ECS/Floating Text/Pressure Test")]
        private static void Open() => GetWindow<FloatingTextStressWindow>("Floating Text");

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Stop();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Stop();
            Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inject events. An empty scene can use the test camera.", MessageType.Info);
                return;
            }

            selectedCamera = (Camera)EditorGUILayout.ObjectField("Camera (optional)", selectedCamera, typeof(Camera), true);
            capacity = Mathf.Clamp(EditorGUILayout.IntField("Pool capacity for next run", capacity), 1, FloatingTextConfig.MaxCapacity);
            frozen = EditorGUILayout.Toggle("Freeze batch", frozen);
            EditorGUILayout.BeginHorizontal();
            foreach (int size in Sizes)
                if (GUILayout.Button(size.ToString())) StartTest(size, false);
            EditorGUILayout.EndHorizontal();

            eventsPerSecond = Mathf.Clamp(EditorGUILayout.IntField("Events per second", eventsPerSecond), 1, 100000);
            merge = EditorGUILayout.Toggle("Repeat mergeable events", merge);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start continuous")) StartTest(0, true);
            using (new EditorGUI.DisabledScope(system == null))
                if (GUILayout.Button("Clear and stop")) Stop();
            EditorGUILayout.EndHorizontal();

            if (system == null) return;
            int active = system.Pool.Slots.Count, limit = system.Pool.Slots.Capacity;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live / capacity", $"{active:n0} / {limit:n0} ({(double)active / limit:p0})");
            EditorGUILayout.LabelField("Glyph quads", system.Batch.Quads.ToString("n0"));
            EditorGUILayout.LabelField("Received / spawned per second", $"{receivedRate:n0} / {spawnedRate:n0}");
            EditorGUILayout.LabelField("Merged / dropped per second", $"{mergedRate:n0} / {droppedRate:n0}");
            EditorGUILayout.LabelField("CPU update / mesh upload", $"{updateMs:f3} ms / {TimeLabel(upload)}");
            EditorGUILayout.LabelField("Managed bytes in update", allocatedBytes.ToString("n0"));
            EditorGUILayout.LabelField("Draw calls / batches (frame)", $"{CounterLabel(draws)} / {CounterLabel(batches)}");
            EditorGUILayout.LabelField("Managed bytes (frame)", CounterLabel(frameGc));
        }

        private static string TimeLabel(ProfilerRecorder recorder) => recorder.Valid ? $"{recorder.LastValue * 1e-6:f3} ms" : "—";
        private static string CounterLabel(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue.ToString("n0") : "—";

        private void StartTest(int burst, bool sustained)
        {
            Stop();
            try
            {
                var config = FloatingTextConfig.Load();
                root = new GameObject("Floating Text Pressure Test") { hideFlags = HideFlags.DontSave };
                view = selectedCamera ? selectedCamera : Camera.main;
                if (!view)
                {
                    var cameraObject = new GameObject("Floating Text Test Camera", typeof(Camera)) { hideFlags = HideFlags.DontSave };
                    cameraObject.transform.SetParent(root.transform, false);
                    cameraObject.transform.position = Vector3.back * 20;
                    view = cameraObject.GetComponent<Camera>();
                    view.orthographic = true;
                    view.orthographicSize = 10;
                    view.clearFlags = CameraClearFlags.SolidColor;
                    view.backgroundColor = new Color(.13f, .12f, .16f);
                }
                system = new FloatingTextSystem(root.transform, config, Mathf.Max(capacity, burst));
                upload = ProfilerRecorder.StartNew(FloatingTextBatchRenderer.UploadMarker);
                draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                frameGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                continuous = sustained;
                fraction = animationTime = 0;
                sequence = 0;
                lastFrame = -1;
                received = spawned = merged = dropped = 0;
                receivedRate = spawnedRate = mergedRate = droppedRate = 0;
                sampleTime = lastTime = Time.unscaledTimeAsDouble;
                if (burst > 0) Inject(burst, false);
                Tick();
            }
            catch (Exception error) { Stop(); Debug.LogException(error); }
        }

        private void Tick()
        {
            if (system == null || !EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
            if (!view) { Stop(); return; }
            bool firstFrame = lastFrame < 0;
            lastFrame = Time.frameCount;
            double now = Time.unscaledTimeAsDouble;
            double delta = now - lastTime;
            if (!frozen) animationTime += delta;
            if (continuous && !frozen)
            {
                // Distribute integral event counts without losing the fractional rate between frames.
                fraction += Math.Min(.25, delta) * eventsPerSecond;
                int count = (int)fraction;
                fraction -= count;
                if (count > 0) Inject(count, merge);
            }
            lastTime = now;

            if (firstFrame || !frozen)
            {
                long start = Stopwatch.GetTimestamp();
                long before = GC.GetAllocatedBytesForCurrentThread();
                system.Tick(animationTime, view);
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                updateMs = (Stopwatch.GetTimestamp() - start) * (1000.0 / Stopwatch.Frequency);
            }
            if (now - sampleTime < .25) return;

            double elapsed = now - sampleTime;
            long totalDropped = system.Events.Dropped + system.Pool.Dropped + system.Pool.Evicted;
            receivedRate = (system.Events.Received - received) / elapsed;
            spawnedRate = (system.Pool.Spawned - spawned) / elapsed;
            mergedRate = (system.Pool.Merged - merged) / elapsed;
            droppedRate = (totalDropped - dropped) / elapsed;
            received = system.Events.Received;
            spawned = system.Pool.Spawned;
            merged = system.Pool.Merged;
            dropped = totalDropped;
            sampleTime = now;
            Repaint();
        }

        private void Inject(int count, bool duplicates)
        {
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count * 1.6f));
            int rows = (count + columns - 1) / columns;
            float depth = Mathf.Lerp(view.nearClipPlane, view.farClipPlane, .5f);
            depth = Mathf.Clamp(view.nearClipPlane + 20, view.nearClipPlane + .01f, depth);
            var bottomLeft = view.ViewportToWorldPoint(new Vector3(.08f, .08f, depth));
            var width = view.ViewportToWorldPoint(new Vector3(.92f, .08f, depth)) - bottomLeft;
            var height = view.ViewportToWorldPoint(new Vector3(.08f, .88f, depth)) - bottomLeft;

            for (int i = 0; i < count; i++)
            {
                int sample = (sequence++ & int.MaxValue) % 1000000;
                int cell = duplicates ? i / 4 : i;
                int source = duplicates ? cell % 128 + 1 : sample + 1;
                Vector3 position = bottomLeft + width * ((cell % columns + .5f) / columns) + height * ((cell / columns + .5f) / rows);
                if (sample % 13 == 0)
                    system.Events.Enqueue(FloatingTextEvent.FromInteger(position, FloatingTextSource.Zone, source,
                        FloatingTextType.Score, sample * 17L));
                else if (sample % 7 == 0)
                    system.Events.Enqueue(new FloatingTextEvent(position, FloatingTextSource.Device, source,
                        FloatingTextType.Multiplier, 1.5 + sample % 9));
                else
                    system.Events.Enqueue(new FloatingTextEvent(position, FloatingTextSource.Device, source,
                        FloatingTextType.Additive, sample % 200 + .25, target: new MarbleKey(1, (ulong)source)));
            }
        }

        private void Stop()
        {
            upload.Dispose();
            draws.Dispose();
            batches.Dispose();
            frameGc.Dispose();
            system?.Dispose();
            system = null;
            if (root)
            {
                if (Application.isPlaying) Destroy(root);
                else DestroyImmediate(root);
            }
            root = null;
            view = null;
        }
    }
}
