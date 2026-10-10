using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace MRes
{
    internal sealed class MResMeshData
    {
        internal string Name;
        internal IndexFormat IndexFormat;
        internal Vector3[] Vertices, Normals;
        internal Vector4[] Tangents;
        internal Vector2[] Uv0, Uv1;
        internal Color[] Colors;
        internal int[][] Indices;
        internal MeshTopology[] Topologies;
        internal Bounds Bounds;
    }

    internal static class MResMeshReader
    {
        internal static MResMeshData Parse(byte[] bytes, string path)
        {
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var r = new BinaryReader(stream))
                {
                    if (r.ReadUInt32() != 0x48534D4D || r.ReadUInt16() != 1) throw new MResFormatException("Expected MMSH v1: " + path);
                    byte indexFormat = r.ReadByte(), flags = r.ReadByte();
                    uint vertexCount = r.ReadUInt32(), submeshCount = r.ReadUInt32();
                    int stride = 12 + ((flags & 1) != 0 ? 12 : 0) + ((flags & 2) != 0 ? 16 : 0) +
                        ((flags & 4) != 0 ? 8 : 0) + ((flags & 8) != 0 ? 8 : 0) + ((flags & 16) != 0 ? 16 : 0);
                    if (indexFormat > 1 || (flags & ~31) != 0 || vertexCount == 0 || vertexCount > MResFormat.MaxVertices ||
                        (indexFormat == 0 && vertexCount > 65535) || submeshCount == 0 || submeshCount > 1024 ||
                        24L + (long)vertexCount * stride + submeshCount * 8L > stream.Length - stream.Position)
                        throw new MResFormatException("Invalid mesh counts/flags: " + path);
                    Vector3 center = V3(r), extents = V3(r);
                    if (extents.x < 0 || extents.y < 0 || extents.z < 0) throw new MResFormatException("Negative bounds: " + path);
                    int n = (int)vertexCount;
                    var d = new MResMeshData { Name = path, IndexFormat = (IndexFormat)indexFormat,
                        Vertices = new Vector3[n], Bounds = new Bounds(center, extents * 2),
                        Indices = new int[submeshCount][], Topologies = new MeshTopology[submeshCount] };
                    for (int i = 0; i < n; i++) d.Vertices[i] = V3(r);
                    if ((flags & 1) != 0) { d.Normals = new Vector3[n]; for (int i = 0; i < n; i++) d.Normals[i] = V3(r); }
                    if ((flags & 2) != 0) { d.Tangents = new Vector4[n]; for (int i = 0; i < n; i++) d.Tangents[i] = V4(r); }
                    if ((flags & 4) != 0) { d.Uv0 = new Vector2[n]; for (int i = 0; i < n; i++) d.Uv0[i] = V2(r); }
                    if ((flags & 8) != 0) { d.Uv1 = new Vector2[n]; for (int i = 0; i < n; i++) d.Uv1[i] = V2(r); }
                    if ((flags & 16) != 0) { d.Colors = new Color[n]; for (int i = 0; i < n; i++) d.Colors[i] = new Color(F(r), F(r), F(r), F(r)); }
                    long total = 0;
                    for (int s = 0; s < submeshCount; s++)
                    {
                        int topology = r.ReadInt32(); uint count = r.ReadUInt32(); total += count;
                        if ((topology != 0 && topology != 2 && topology != 3 && topology != 4 && topology != 5) ||
                            total > MResFormat.MaxIndices || (long)count * (indexFormat == 0 ? 2 : 4) > stream.Length - stream.Position ||
                            (topology == 0 && count % 3 != 0) || (topology == 2 && count % 4 != 0) ||
                            (topology == 3 && count % 2 != 0) || (topology == 4 && count < 2))
                            throw new MResFormatException("Invalid submesh: " + path);
                        d.Topologies[s] = (MeshTopology)topology; d.Indices[s] = new int[count];
                        for (int i = 0; i < count; i++)
                        {
                            uint index = indexFormat == 0 ? r.ReadUInt16() : r.ReadUInt32();
                            if (index >= vertexCount) throw new MResFormatException("Mesh index out of range: " + path);
                            d.Indices[s][i] = (int)index;
                        }
                    }
                    if (stream.Position != stream.Length) throw new MResFormatException("Trailing mesh bytes: " + path);
                    return d;
                }
            }
            catch (EndOfStreamException e) { throw new MResFormatException("Truncated mesh: " + path, e); }
        }
        private static float F(BinaryReader r)
        {
            float v = r.ReadSingle();
            if (float.IsNaN(v) || float.IsInfinity(v)) throw new MResFormatException("Non-finite mesh value."); return v;
        }
        private static Vector2 V2(BinaryReader r) => new Vector2(F(r), F(r));
        private static Vector3 V3(BinaryReader r) => new Vector3(F(r), F(r), F(r));
        private static Vector4 V4(BinaryReader r) => new Vector4(F(r), F(r), F(r), F(r));
        internal static Mesh Create(MResMeshData d, MResLoadHandle handle)
        {
            var mesh = handle.Own(new Mesh { name = d.Name, indexFormat = d.IndexFormat });
            mesh.vertices = d.Vertices;
            if (d.Normals != null) mesh.normals = d.Normals;
            if (d.Tangents != null) mesh.tangents = d.Tangents;
            if (d.Uv0 != null) mesh.uv = d.Uv0;
            if (d.Uv1 != null) mesh.uv2 = d.Uv1;
            if (d.Colors != null) mesh.colors = d.Colors;
            mesh.subMeshCount = d.Indices.Length;
            for (int s = 0; s < d.Indices.Length; s++) mesh.SetIndices(d.Indices[s], d.Topologies[s], s, false);
            mesh.bounds = d.Bounds;
            return mesh;
        }
    }
}
