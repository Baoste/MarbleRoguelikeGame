using System;
using System.Runtime.InteropServices;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarblesECS
{
    internal sealed class FloatingTextBatchRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            internal Vector3 Position;
            internal Color32 Color;
            internal Vector2 UV;

            internal Vertex(Vector3 position, Color32 color, float u, float v)
            { Position = position; Color = color; UV = new Vector2(u, v); }
        }

        private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("FloatingText.BuildMesh");
        internal static readonly ProfilerMarker UploadMarker = new ProfilerMarker("FloatingText.UploadMesh");
        private const MeshUpdateFlags UpdateFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
        private readonly Vertex[] vertices;
        private readonly GameObject root;
        private readonly MeshRenderer renderer;
        private readonly Mesh mesh;
        private readonly int maxCharacters;
        private readonly float widestGlyph;

        internal int Quads { get; private set; }
        internal Mesh Mesh => mesh;
        internal MeshRenderer Renderer => renderer;

        internal FloatingTextBatchRenderer(Transform owner, FloatingTextConfig config, FloatingTextAtlas atlas, int capacity)
        {
            maxCharacters = config.MaxCharacters;
            widestGlyph = atlas.WidestGlyph;
            int glyphCapacity = capacity * maxCharacters;
            vertices = new Vertex[glyphCapacity * 4];
            mesh = new Mesh { name = "Floating Text Batch", hideFlags = HideFlags.DontSave };
            try
            {
                InitializeMesh(glyphCapacity, mesh);
                root = new GameObject("Floating Text Batch", typeof(MeshFilter), typeof(MeshRenderer));
                root.hideFlags = HideFlags.DontSave;
                root.transform.SetParent(owner, false);
                root.layer = config.Layer;
                root.GetComponent<MeshFilter>().sharedMesh = mesh;
                renderer = root.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = atlas.Material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.sortingLayerName = config.SortingLayer;
                renderer.sortingOrder = config.SortingOrder;
                renderer.enabled = false;
            }
            catch { Dispose(); throw; }
        }

        private static void InitializeMesh(int glyphCapacity, Mesh mesh)
        {
            int vertexCapacity = glyphCapacity * 4;
            int indexCapacity = glyphCapacity * 6;
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertexCapacity,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            // Avoid index 0xffff, which is reserved on some graphics backends.
            bool useShort = vertexCapacity <= ushort.MaxValue;
            mesh.SetIndexBufferParams(indexCapacity, useShort ? IndexFormat.UInt16 : IndexFormat.UInt32);
            if (useShort)
            {
                var indices = new ushort[indexCapacity];
                for (int i = 0; i < glyphCapacity; i++)
                {
                    int v = i * 4, j = i * 6;
                    indices[j] = (ushort)v;
                    indices[j + 1] = indices[j + 3] = (ushort)(v + 1);
                    indices[j + 2] = indices[j + 5] = (ushort)(v + 2);
                    indices[j + 4] = (ushort)(v + 3);
                }
                mesh.SetIndexBufferData(indices, 0, 0, indexCapacity, UpdateFlags);
            }
            else
            {
                var indices = new int[indexCapacity];
                for (int i = 0; i < glyphCapacity; i++)
                {
                    int v = i * 4, j = i * 6;
                    indices[j] = v;
                    indices[j + 1] = indices[j + 3] = v + 1;
                    indices[j + 2] = indices[j + 5] = v + 2;
                    indices[j + 4] = v + 3;
                }
                mesh.SetIndexBufferData(indices, 0, 0, indexCapacity, UpdateFlags);
            }
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0), UpdateFlags);
        }

        internal void Render(FloatingTextPool pool, Camera camera, double now)
        {
            if (pool.Slots.Count == 0 || !camera) { Clear(); return; }
            int vertexCount;
            Bounds bounds;
            using (BuildMarker.Auto()) vertexCount = BuildVertices(pool, camera.transform, now, out bounds);
            if (vertexCount == 0) { Clear(); return; }
            using (UploadMarker.Auto())
            {
                mesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, UpdateFlags);
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, vertexCount / 4 * 6)
                { vertexCount = vertexCount, bounds = bounds }, UpdateFlags);
                mesh.bounds = bounds;
                Quads = vertexCount / 4;
                if (!renderer.enabled) renderer.enabled = true;
            }
        }

        private int BuildVertices(FloatingTextPool pool, Transform camera, double now, out Bounds bounds)
        {
            Matrix4x4 worldToLocal = root.transform.worldToLocalMatrix;
            Vector3 right = worldToLocal.MultiplyVector(camera.right);
            Vector3 up = worldToLocal.MultiplyVector(camera.up);
            Vector3 towardCamera = worldToLocal.MultiplyVector(-camera.forward);
            Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
            int count = 0;
            var slots = pool.Slots;
            // Higher priority labels are composited last within the same draw.
            for (int priority = slots.LowestPriority; priority <= byte.MaxValue; priority++)
                for (int slot = slots.FirstAt(priority); slot >= 0; slot = slots.NextAt(slot))
                {
                    ref var item = ref pool.Items[slot];
                    var style = item.Style;
                    var animation = new FloatingTextAnimation(item, now);
                    if (animation.Color.a == 0 || animation.Height <= 0) continue;
                    Vector3 center = worldToLocal.MultiplyPoint3x4(item.Position) +
                        right * animation.Offset.x + up * animation.Offset.y + towardCamera * animation.Offset.z;
                    Vector3 x = right * animation.Height, y = up * animation.Height;
                    Vector3 extent = (Abs(x) * item.Width + Abs(y)) * .5f;
                    min = Vector3.Min(min, center - extent);
                    max = Vector3.Max(max, center + extent);
                    Vector3 position = center - (x * item.Width + y) * .5f;
                    int start = slot * maxCharacters;
                    for (int i = 0; i < item.GlyphCount; i++)
                    {
                        var glyph = pool.Glyphs[start + i];
                        float advance = style.Monospace ? widestGlyph : glyph.Width;
                        Vector3 left = position + x * ((advance - glyph.Width) * .5f);
                        Vector3 glyphRight = left + x * glyph.Width;
                        var uv = glyph.UV;
                        vertices[count++] = new Vertex(left, animation.Color, uv.x, uv.y);
                        vertices[count++] = new Vertex(glyphRight, animation.Color, uv.z, uv.y);
                        vertices[count++] = new Vertex(left + y, animation.Color, uv.x, uv.w);
                        vertices[count++] = new Vertex(glyphRight + y, animation.Color, uv.z, uv.w);
                        position += x * (advance + style.Spacing);
                    }
                }
            bounds = default;
            if (count != 0)
            {
                bounds.SetMinMax(min, max);
                bounds.Expand(.01f);
            }
            return count;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        internal void Clear()
        {
            if (Quads == 0) return;
            Quads = 0;
            if (renderer) renderer.enabled = false;
            if (mesh) mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0), UpdateFlags);
        }

        public void Dispose()
        {
            if (renderer) renderer.enabled = false;
            Destroy(root);
            Destroy(mesh);
        }

        private static void Destroy(UnityEngine.Object item)
        {
            if (!item) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
