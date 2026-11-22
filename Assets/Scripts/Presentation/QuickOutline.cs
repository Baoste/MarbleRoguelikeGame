// Adapted from QuickOutline by Chris Nolet, copyright © 2018 Chris Nolet.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MarblesECS.Presentation
{
    internal sealed class QuickOutline : IDisposable
    {
        private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
        private readonly List<Object> owned = new List<Object>();
        private readonly Material fill;
        private Color currentColor;

        internal QuickOutline(GameObject root)
        {
            var maskShader = Resources.Load<Shader>("QuickOutline/OutlineMask");
            var fillShader = Resources.Load<Shader>("QuickOutline/OutlineFill");
            if (!maskShader || !fillShader)
            {
                Debug.LogError("QuickOutline shaders are missing.", root);
                return;
            }

            try
            {
                var mask = Own(new Material(maskShader));
                fill = Own(new Material(fillShader));
                var materials = new[] { mask, fill };
                var meshes = new Dictionary<Mesh, Mesh>();
                foreach (var source in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!source.enabled || !source.TryGetComponent<MeshFilter>(out var filter)) continue;
                    var mesh = filter.sharedMesh;
                    if (!mesh || !mesh.isReadable) continue;

                    if (!meshes.TryGetValue(mesh, out var outlineMesh))
                    {
                        outlineMesh = CreateOutlineMesh(mesh);
                        meshes.Add(mesh, outlineMesh);
                    }
                    if (!outlineMesh) continue;

                    var child = Own(new GameObject("QuickOutline", typeof(MeshFilter), typeof(MeshRenderer)));
                    child.layer = source.gameObject.layer;
                    child.transform.SetParent(source.transform, false);
                    child.GetComponent<MeshFilter>().sharedMesh = outlineMesh;
                    var renderer = child.GetComponent<MeshRenderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.sharedMaterials = materials;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void SetColor(Color color)
        {
            if (!fill || currentColor == color) return;
            currentColor = color;
            fill.SetColor(OutlineColor, color);
        }

        private Mesh CreateOutlineMesh(Mesh source)
        {
            // Both passes use the full silhouette. The device's mesh and material slots stay intact.
            var triangles = new List<int>();
            for (int i = 0; i < source.subMeshCount; i++)
            {
                var topology = source.GetTopology(i);
                if (topology == MeshTopology.Triangles || topology == MeshTopology.Quads)
                    triangles.AddRange(source.GetTriangles(i));
            }
            if (triangles.Count == 0) return null;

            var vertices = source.vertices;
            var outline = Own(new Mesh { name = source.name + " Outline", indexFormat = source.indexFormat });
            outline.vertices = vertices;
            outline.SetTriangles(triangles, 0, false);
            outline.bounds = source.bounds;

            var normals = source.normals;
            if (normals.Length != vertices.Length)
            {
                outline.RecalculateNormals();
                normals = outline.normals;
            }
            // Average normals at matching positions so hard edges and UV seams expand together.
            var sums = new Dictionary<Vector3, Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                sums.TryGetValue(vertices[i], out var sum);
                sums[vertices[i]] = sum + normals[i];
            }
            for (int i = 0; i < vertices.Length; i++)
            {
                var normal = sums[vertices[i]].normalized;
                if (normal != Vector3.zero) normals[i] = normal;
            }
            outline.SetUVs(3, normals);
            return outline;
        }

        private T Own<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }

        public void Dispose()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var item = owned[i];
                if (!item) continue;
                if (item is GameObject child) child.SetActive(false);
                if (Application.isPlaying) Object.Destroy(item);
                else Object.DestroyImmediate(item);
            }
            owned.Clear();
        }
    }
}
