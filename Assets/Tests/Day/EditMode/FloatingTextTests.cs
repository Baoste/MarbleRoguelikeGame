using System;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MarblesECS.Tests
{
    public sealed class FloatingTextTests
    {
        [Test]
        public void FormatsNumbersAndPreservesExactIntegersBeforeAbbreviation()
        {
            var config = FloatingTextConfig.Load();
            var style = config.Styles[2];
            var exact = new FloatingTextStyle(ConfigJson.Read(@"{""id"":75,""priority"":1,""lifetime"":1,
                ""height"":1,""color"":""#FFFFFF"",""abbreviateAt"":0}", "test"), 24);
            var compact = new FloatingTextStyle(ConfigJson.Read(@"{""id"":76,""priority"":1,""lifetime"":1,
                ""height"":1,""color"":""#FFFFFF"",""abbreviateAt"":1000}", "test"), 12);
            Assert.That(Format(new FloatingTextValue(10L), FloatingTextType.Additive, style), Is.EqualTo("+10"));
            Assert.That(Format(new FloatingTextValue(-5L), FloatingTextType.Additive, style), Is.EqualTo("-5"));
            Assert.That(Format(new FloatingTextValue(1.5), FloatingTextType.Multiplier, style), Is.EqualTo("×1.5"));
            Assert.That(Format(new FloatingTextValue(1000L), FloatingTextType.Score, style), Is.EqualTo("+1000"));
            Assert.That(Format(new FloatingTextValue(1500L), FloatingTextType.Score, exact, 6), Is.EqualTo("+1500"));
            Assert.That(Format(new FloatingTextValue(1500L), FloatingTextType.Score, compact), Is.EqualTo("+1.5K"));
            Assert.That(Format(new FloatingTextValue(15_000L), FloatingTextType.Score, style), Is.EqualTo("+15K"));
            Assert.That(Format(new FloatingTextValue(1_500_000L), FloatingTextType.Score, style), Is.EqualTo("+1.5M"));
            Assert.That(Format(new FloatingTextValue(999_999L), FloatingTextType.Score, style), Is.EqualTo("+1M"));
            Assert.That(Format(new FloatingTextValue(9007199254740993L), FloatingTextType.Score, exact, 24), Is.EqualTo("+9007199254740993"));
            Assert.That(Format(new FloatingTextValue(long.MinValue), FloatingTextType.Score, exact, 24), Is.EqualTo("-9223372036854775808"));
            Assert.That(Format(new FloatingTextValue(double.MaxValue), FloatingTextType.Score, style), Does.EndWith("e308"));
        }

        private static string Format(FloatingTextValue value, FloatingTextType type, FloatingTextStyle style, int length = 12)
        {
            Span<char> output = stackalloc char[length];
            int written = FloatingTextFormatter.Write(value, type, style, output);
            return new string(output.Slice(0, written));
        }

        [Test]
        public void MergesOnlyMatchingSourcesStylesAndTargetsInsideWindow()
        {
            using var view = new View(16);
            var system = view.System;
            var same = new MarbleKey(1, 1);
            var different = new MarbleKey(1, 2);
            var add = new FloatingTextEvent(Vector3.zero, FloatingTextSource.Device, 42, FloatingTextType.Additive, 10, same);
            system.Events.Enqueue(add);
            system.Tick(0, view.Camera);
            Assert.That(system.Pool.Slots.Count, Is.EqualTo(1), "first feedback is immediate");
            system.Events.Enqueue(add);
            system.Events.Enqueue(add);
            system.Events.Enqueue(new FloatingTextEvent(Vector3.zero, FloatingTextSource.Device, 42, FloatingTextType.Additive, 10, different));
            system.Events.Enqueue(new FloatingTextEvent(Vector3.zero, FloatingTextSource.Pin, 42, FloatingTextType.Additive, 10, same));
            system.Events.Enqueue(new FloatingTextEvent(Vector3.zero, FloatingTextSource.Device, 42, FloatingTextType.Additive, 10, same, 6));
            var factor = new FloatingTextEvent(Vector3.zero, FloatingTextSource.Device, 42, FloatingTextType.Multiplier, 1.5, same);
            system.Events.Enqueue(factor);
            system.Events.Enqueue(factor);
            system.Tick(.05, view.Camera);
            Assert.That(system.Pool.Slots.Count, Is.EqualTo(6));
            Assert.That(system.Pool.Items[system.Pool.Slots.First].Value.Number, Is.EqualTo(30));
            Assert.That(system.Pool.Merged, Is.EqualTo(2));
            system.Events.Enqueue(add);
            system.Tick(.2, view.Camera);
            Assert.That(system.Pool.Slots.Count, Is.EqualTo(7));
            system.Tick(2, view.Camera);
            Assert.That(system.Pool.Slots.Count, Is.Zero);
            Assert.That(system.Batch.Mesh.GetSubMesh(0).indexCount, Is.Zero);
            Assert.That(system.Batch.Renderer.enabled, Is.False);
        }

        [Test]
        public void MergePrecedesEvictionAndScoreReplacesOldestLowestPriorityLabel()
        {
            using var view = new View(3);
            var system = view.System;
            var baseEvent = new FloatingTextEvent(Vector3.zero, FloatingTextSource.Device, 1, FloatingTextType.Additive, 10, new MarbleKey(1, 1));
            var first = new FloatingTextEvent(Vector3.left, FloatingTextSource.Pin, 2, FloatingTextType.Multiplier, 2);
            var second = new FloatingTextEvent(Vector3.right, FloatingTextSource.Pin, 3, FloatingTextType.Multiplier, 2);
            system.Events.Enqueue(baseEvent);
            system.Events.Enqueue(first);
            system.Events.Enqueue(second);
            system.Tick(0, view.Camera);
            system.Events.Enqueue(baseEvent);
            system.Events.Enqueue(first);
            system.Events.Enqueue(FloatingTextEvent.FromInteger(Vector3.zero, FloatingTextSource.Zone, 4, FloatingTextType.Score, 300));
            system.Tick(.01, view.Camera);

            var slots = system.Pool.Slots;
            Assert.That(slots.Count, Is.EqualTo(3));
            Assert.That(system.Pool.Merged, Is.EqualTo(1));
            Assert.That(system.Pool.Dropped, Is.EqualTo(1));
            Assert.That(system.Pool.Evicted, Is.EqualTo(1));
            Assert.That(system.Pool.Items[slots.Next(slots.First)].Position, Is.EqualTo(Vector3.right));
            system.Tick(.02, view.Camera, scoresOnly: true);
            Assert.That(slots.Count, Is.EqualTo(1));
            Assert.That(system.Pool.Items[slots.First].Type, Is.EqualTo(FloatingTextType.Score));
            system.Events.Reset();
            system.Tick(.03, view.Camera);
            Assert.That(slots.Count, Is.Zero);
            Assert.That(system.Batch.Quads, Is.Zero);
        }

        [Test]
        public void PendingBufferProtectsScoresDuringOverflow()
        {
            var events = new FloatingTextEventBuffer(FloatingTextConfig.Load(), 2);
            var pin = new FloatingTextEvent(Vector3.zero, FloatingTextSource.Pin, 1, FloatingTextType.Multiplier, 2);
            var score = FloatingTextEvent.FromInteger(Vector3.zero, FloatingTextSource.Zone, 2, FloatingTextType.Score, 100);
            events.Enqueue(pin);
            events.Enqueue(pin);
            events.Enqueue(score);
            events.Enqueue(pin);
            Assert.That(events.Received, Is.EqualTo(4));
            Assert.That(events.Dropped, Is.EqualTo(2));
            Assert.That(events.Get(events.First).Event.Type, Is.EqualTo(FloatingTextType.Multiplier));
            Assert.That(events.Get(events.Next(events.First)).Event.Type, Is.EqualTo(FloatingTextType.Score));
        }

        [Test]
        public void ConfigurationCanFilterMergingByTypeAndSourceAndShowNeutralHits()
        {
            string json = Resources.Load<TextAsset>(FloatingTextConfig.ResourcePath).text
                .Replace("\"showNeutralPins\": false", "\"showNeutralPins\": true")
                .Replace("[\"Additive\", \"Currency\", \"Blood\"]", "[\"Additive\"]")
                .Replace("\"unmergedSources\": []", "\"unmergedSources\": [{\"source\":\"Device\",\"id\":42}]");
            var asset = new TextAsset(json);
            try
            {
                var config = FloatingTextConfig.Load(asset);
                var events = new FloatingTextEventBuffer(config, 3);
                var blocked = new FloatingTextEvent(Vector3.zero, FloatingTextSource.Device, 42, FloatingTextType.Additive, 10);
                var other = new FloatingTextEvent(Vector3.zero, FloatingTextSource.Device, 41, FloatingTextType.Additive, 10);
                var currency = FloatingTextEvent.FromInteger(Vector3.zero, FloatingTextSource.Device, 41, FloatingTextType.Currency, 10);
                var neutral = new FloatingTextEvent(Vector3.zero, FloatingTextSource.Pin, 1, FloatingTextType.PinContact, 0);
                Assert.That(config.CanMerge(blocked), Is.False);
                Assert.That(config.CanMerge(other), Is.True);
                Assert.That(config.CanMerge(currency), Is.False);
                events.Enqueue(neutral);
                Assert.That(events.Get(events.First).Event.Type, Is.EqualTo(FloatingTextType.PinContact));
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [TestCase(100)]
        [TestCase(500)]
        [TestCase(1000)]
        [TestCase(2000)]
        public void BurstUsesOneSubMeshAndExpectedIndexFormat(int count)
        {
            using var view = new View(count);
            var system = view.System;
            for (int i = 0; i < count; i++)
            {
                Vector3 position = new Vector3(i % 50 * .05f, i / 50 * .05f, 0);
                system.Events.Enqueue(FloatingTextEvent.FromInteger(position, FloatingTextSource.Zone, 1, FloatingTextType.Score, 1200));
            }
            system.Tick(0, view.Camera);
            var mesh = system.Batch.Mesh;
            Assert.That(system.Pool.Slots.Count, Is.EqualTo(count));
            Assert.That(system.Batch.Quads, Is.EqualTo(count * 5));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.GetSubMesh(0).indexCount, Is.EqualTo(count * 5 * 6));
            Assert.That(mesh.indexFormat, Is.EqualTo(count == 2000 ? IndexFormat.UInt32 : IndexFormat.UInt16));
            Vector3 local = system.Batch.Renderer.transform.InverseTransformPoint(Vector3.up * .17f + Vector3.back * .06f);
            Assert.That(mesh.bounds.Contains(local), Is.True);
            Assert.That(system.Batch.Renderer.sharedMaterials.Length, Is.EqualTo(1));
            system.ClearAll();
            Assert.That(system.Batch.Mesh, Is.SameAs(mesh));
            Assert.That(system.Batch.Quads, Is.Zero);
        }

        [Test]
        public void WarmedContinuousLoadReusesBuffersWithoutManagedAllocations()
        {
            using var view = new View(2000);
            var system = view.System;
            var mesh = system.Batch.Mesh;
            var items = system.Pool.Items;
            var glyphs = system.Pool.Glyphs;
            for (int frame = 0; frame < 60; frame++) InjectFrame(view, frame);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int frame = 60; frame < 660; frame++) InjectFrame(view, frame);
            long ticks = Stopwatch.GetTimestamp() - start;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Debug.Log($"FLOATING_TEXT_STRESS 160 events/frame × 600 frames, {allocated} managed B, " +
                $"{ticks * 1000.0 / Stopwatch.Frequency / 600:f3} ms/frame CPU, live={system.Pool.Slots.Count}, " +
                $"quads={system.Batch.Quads}, received={system.Events.Received}, spawned={system.Pool.Spawned}, " +
                $"merged={system.Pool.Merged}, dropped={system.Events.Dropped + system.Pool.Dropped}, evicted={system.Pool.Evicted}");
            Assert.That(allocated, Is.Zero);
            Assert.That(system.Batch.Mesh, Is.SameAs(mesh));
            Assert.That(system.Pool.Items, Is.SameAs(items));
            Assert.That(system.Pool.Glyphs, Is.SameAs(glyphs));
            Assert.That(system.Pool.Merged, Is.GreaterThan(0));
            Assert.That(system.Pool.Dropped + system.Pool.Evicted, Is.GreaterThan(0));
            system.Tick(20, view.Camera);
            Assert.That(system.Batch.Quads, Is.Zero);
        }

        private static void InjectFrame(View view, int frame)
        {
            var events = view.System.Events;
            for (int i = 0; i < 160; i++)
            {
                int source = frame * 160 + i + 1;
                Vector3 position = new Vector3(i % 20 * .1f, i / 20 * .1f, 0);
                if (i % 17 == 0)
                    events.Enqueue(FloatingTextEvent.FromInteger(position, FloatingTextSource.Zone, source, FloatingTextType.Score, source));
                else if (i % 5 == 0)
                    events.Enqueue(new FloatingTextEvent(position, FloatingTextSource.Device, source, FloatingTextType.Multiplier, 1.5 + i % 8));
                else
                    events.Enqueue(new FloatingTextEvent(position, FloatingTextSource.Device, i / 2 + 1, FloatingTextType.Additive,
                        i + .125, new MarbleKey(1, (ulong)(i / 2 + 1))));
            }
            view.System.Tick(frame / 60.0, view.Camera);
        }

        [Test]
        public void UrpDrawsAtlasOnTopOfOpaqueGeometryAndFadesIt()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires Unity graphics device.");
            using var view = new View(4);
            view.Camera.cullingMask = 1 << 30;
            view.Camera.orthographicSize = 1.4f;
            var system = view.System;
            var renderer = system.Batch.Renderer;
            renderer.gameObject.layer = 30;
            Assert.That(renderer.sharedMaterial.shader.isSupported, Is.True);
            Assert.That(renderer.sharedMaterial.passCount, Is.EqualTo(1));
            var target = new RenderTexture(320, 240, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                cover.layer = 30;
                cover.transform.SetPositionAndRotation(Vector3.back, Quaternion.identity);
                cover.transform.localScale = new Vector3(10, 10, .1f);
                material.SetColor("_BaseColor", Color.black);
                cover.GetComponent<Renderer>().sharedMaterial = material;
                view.Camera.targetTexture = target;
                int baseline = BrightPixels(view.Camera, target, pixels);
                system.Events.Enqueue(FloatingTextEvent.FromInteger(Vector3.zero, FloatingTextSource.Zone, 1, FloatingTextType.Score, 123));
                system.Tick(0, view.Camera);
                system.Tick(.18, view.Camera);
                int opaque = BrightPixels(view.Camera, target, pixels);
                system.Tick(.76, view.Camera);
                int faded = BrightPixels(view.Camera, target, pixels);
                system.Tick(1, view.Camera);
                Assert.That(opaque, Is.GreaterThan(baseline + 100));
                Assert.That(faded, Is.LessThan(opaque));
                Assert.That(BrightPixels(view.Camera, target, pixels), Is.EqualTo(baseline));
                Debug.Log($"FLOATING_TEXT_RENDER {SystemInfo.graphicsDeviceType}: bright pixels before={baseline}, shown={opaque}, faded={faded}");
            }
            finally
            {
                view.Camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(cover);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
            }
        }

        private static int BrightPixels(Camera camera, RenderTexture target, Texture2D pixels)
        {
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            int count = 0;
            foreach (var pixel in pixels.GetRawTextureData<Color32>())
                if (pixel.r > 160 && pixel.g > 145) count++;
            return count;
        }

        private sealed class View : IDisposable
        {
            private readonly GameObject root;
            internal readonly FloatingTextSystem System;
            internal readonly Camera Camera;

            internal View(int capacity)
            {
                root = new GameObject("Floating text tests", typeof(Camera)) { hideFlags = HideFlags.DontSave };
                root.transform.position = Vector3.back * 10;
                Camera = root.GetComponent<Camera>();
                Camera.enabled = false;
                Camera.allowHDR = false;
                Camera.orthographic = true;
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = Color.black;
                try { System = new FloatingTextSystem(root.transform, FloatingTextConfig.Load(), capacity); }
                catch { Object.DestroyImmediate(root); throw; }
            }

            public void Dispose()
            {
                System.Dispose();
                Object.DestroyImmediate(root);
            }
        }
    }
}
