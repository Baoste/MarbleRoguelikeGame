using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace MResPacker
{
    internal static class MResMeshSerializer
    {
        internal static byte[] Serialize(Mesh mesh)
        {
            if (!mesh.isReadable) throw new ArgumentException("Mesh is not readable: " + mesh.name + ". Enable Read/Write on its importer.");
            if (mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0 || mesh.boneWeights.Length != 0)
                throw new ArgumentException("Skinned mesh/bones/blend shapes are unsupported: " + mesh.name);
            int n = mesh.vertexCount;
            if (n == 0 || n > 1000000 || (mesh.indexFormat == IndexFormat.UInt16 && n > 65535) || mesh.subMeshCount < 1 || mesh.subMeshCount > 1024)
                throw new ArgumentException("Invalid mesh vertex/submesh count: " + mesh.name);
            var vertices = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents;
            var uv0 = mesh.uv; var uv1 = mesh.uv2; var colors = mesh.colors;
            byte flags = 0;
            if (Has(normals.Length, n)) flags |= 1; if (Has(tangents.Length, n)) flags |= 2;
            if (Has(uv0.Length, n)) flags |= 4; if (Has(uv1.Length, n)) flags |= 8; if (Has(colors.Length, n)) flags |= 16;
            // v1 UV0/UV1 have two components; reject rather than truncate larger channels.
            if ((mesh.HasVertexAttribute(VertexAttribute.TexCoord0) && mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) != 2) ||
                (mesh.HasVertexAttribute(VertexAttribute.TexCoord1) && mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1) != 2))
                throw new ArgumentException("v1 UV0/UV1 must be Vector2: " + mesh.name);
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(0x48534D4Du); w.Write((ushort)1); w.Write((byte)mesh.indexFormat); w.Write(flags);
                w.Write((uint)n); w.Write((uint)mesh.subMeshCount);
                var bounds = mesh.bounds;
                if (bounds.extents.x < 0 || bounds.extents.y < 0 || bounds.extents.z < 0) throw new ArgumentException("Negative mesh bounds.");
                V3(w, bounds.center); V3(w, bounds.extents);
                foreach (var v in vertices) V3(w, v); foreach (var v in normals) V3(w, v); foreach (var v in tangents) V4(w, v);
                foreach (var v in uv0) { F(w, v.x); F(w, v.y); } foreach (var v in uv1) { F(w, v.x); F(w, v.y); }
                foreach (var v in colors) { F(w, v.r); F(w, v.g); F(w, v.b); F(w, v.a); }
                long total = 0;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var topology = mesh.GetTopology(i); var indices = mesh.GetIndices(i, true); total += indices.Length;
                    if (total > 6000000 || !ValidTopology(topology, indices.Length)) throw new ArgumentException("Invalid mesh topology/index count.");
                    w.Write((int)topology); w.Write((uint)indices.Length);
                    foreach (int index in indices)
                    {
                        if (index < 0 || index >= n) throw new ArgumentException("Mesh index out of bounds.");
                        if (mesh.indexFormat == IndexFormat.UInt16) w.Write(checked((ushort)index)); else w.Write((uint)index);
                    }
                }
                return stream.ToArray();
            }
        }
        private static bool Has(int length, int n) { if (length != 0 && length != n) throw new ArgumentException("Mesh channel length mismatch."); return length != 0; }
        private static bool ValidTopology(MeshTopology t, int n)
        {
            switch (t) { case MeshTopology.Triangles: return n % 3 == 0; case MeshTopology.Quads: return n % 4 == 0;
                case MeshTopology.Lines: return n % 2 == 0; case MeshTopology.LineStrip: return n >= 2; case MeshTopology.Points: return true; default: return false; }
        }
        private static void F(BinaryWriter w, float v) { if (float.IsNaN(v) || float.IsInfinity(v)) throw new ArgumentException("Non-finite mesh value."); w.Write(v); }
        private static void V3(BinaryWriter w, Vector3 v) { F(w, v.x); F(w, v.y); F(w, v.z); }
        private static void V4(BinaryWriter w, Vector4 v) { F(w, v.x); F(w, v.y); F(w, v.z); F(w, v.w); }
    }
}
