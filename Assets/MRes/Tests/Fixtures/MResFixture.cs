using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MRes.Tests
{
    // Fixed acceptance-test data only. This is not a prefab exporter or a production packer.
    public static class MResFixture
    {
        public const string Transform = "\"transform\":{\"localPosition\":{\"x\":0,\"y\":0,\"z\":0},\"localRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"localScale\":{\"x\":1,\"y\":1,\"z\":1}}";
        public const string Box = "{\"type\":\"BoxCollider\",\"size\":{\"x\":4,\"y\":0.5,\"z\":4},\"center\":{\"x\":0,\"y\":-0.25,\"z\":0},\"contactOffset\":0.03,\"physicsMaterial\":\"physics_materials/0.json\",\"includeLayers\":4,\"excludeLayers\":8,\"layerOverridePriority\":3,\"providesContacts\":true,\"hasModifiableContacts\":true}";
        public const string Metadata = "{\"name\":\"弹射装置\",\"description\":\"碰撞后将目标向上弹射。\",\"value\":120,\"type\":\"device\"}";
        public const string BounceLua = "function OnCollisionEnter(other) other:AddForce(0, 180, 0) end";
        public static string Node(int id, int parent, string name, string components = "", string transform = null, bool active = true)
            => "{\"id\":" + id + ",\"parentId\":" + parent + ",\"name\":\"" + name + "\",\"active\":" + (active ? "true" : "false") + "," +
                (transform ?? Transform) + ",\"components\":[" + components + "]}";

        public static byte[] Create(string lua = null, string metadata = null, string nodes = null, string material = null)
        {
            var entries = new List<Tuple<string, MResEntryType, byte[]>>();
            string manifest = "{\"format\":\"mres\",\"version\":1,\"packageName\":\"BouncePad\",\"prefab\":\"prefab/root.json\",\"metadata\":\"metadata/metadata.json\",\"meshes\":[\"meshes/0.meshbin\"],\"materials\":[\"materials/0.json\",\"materials/1.json\"],\"textures\":[\"textures/0.png\"],\"physicsMaterials\":[\"physics_materials/0.json\"],\"textureSettings\":{\"textures/0.png\":{\"name\":\"FixtureTexture\",\"mipChain\":false,\"linear\":true,\"wrapModeU\":1,\"wrapModeV\":2,\"filterMode\":0,\"anisoLevel\":3}}" +
                (lua == null ? "" : ",\"lua\":\"scripts/main.lua\"") + "}";
            Add(entries, "manifest.json", MResEntryType.Manifest, manifest);
            string render = "{\"type\":\"MeshFilter\",\"mesh\":\"meshes/0.meshbin\"},{\"type\":\"MeshRenderer\",\"materials\":[\"materials/0.json\",\"materials/1.json\",null]}";
            if (nodes == null)
            {
                nodes = Node(1, -1, "BouncePad", render + "," + Box) + "," +
                    Node(2, 1, "Child", "{\"type\":\"SphereCollider\",\"radius\":0.7,\"enabled\":false,\"physicsMaterial\":\"physics_materials/0.json\"}") + "," +
                    Node(3, 2, "Grandchild", "{\"type\":\"CapsuleCollider\",\"radius\":0.25,\"height\":1.5,\"direction\":2,\"enabled\":false},{\"type\":\"MeshCollider\",\"mesh\":\"meshes/0.meshbin\",\"convex\":false,\"enabled\":false}",
                    Transform.Replace("\"localPosition\":{\"x\":0,\"y\":0,\"z\":0}", "\"localPosition\":{\"x\":1,\"y\":2,\"z\":3}"), false);
            }
            Add(entries, "prefab/root.json", MResEntryType.Prefab, "{\"nodes\":[" + nodes + "]}");
            Add(entries, "metadata/metadata.json", MResEntryType.Metadata, metadata ?? Metadata);
            Add(entries, "materials/0.json", MResEntryType.Material, material ??
                "{\"name\":\"FixtureMaterial\",\"shaderId\":\"MRes/Unlit\",\"renderQueue\":2100,\"enableInstancing\":true,\"doubleSidedGI\":true,\"keywords\":[\"MRES_TEST\"],\"properties\":[{\"name\":\"_Color\",\"type\":\"Color\",\"value\":{\"r\":0.2,\"g\":0.8,\"b\":0.9,\"a\":1}},{\"name\":\"_MainTex\",\"type\":\"Texture\",\"value\":\"textures/0.png\",\"scale\":{\"x\":2,\"y\":3},\"offset\":{\"x\":0.1,\"y\":0.2}},{\"name\":\"_Amount\",\"type\":\"Float\",\"value\":0.75},{\"name\":\"_Range\",\"type\":\"Range\",\"value\":0.25},{\"name\":\"_Count\",\"type\":\"Int\",\"value\":7},{\"name\":\"_Direction\",\"type\":\"Vector\",\"value\":{\"x\":1,\"y\":2,\"z\":3,\"w\":4}}]}");
            Add(entries, "materials/1.json", MResEntryType.Material, "{\"name\":\"SecondSlot\",\"shaderId\":\"MRes/Unlit\",\"properties\":[{\"name\":\"_Color\",\"type\":\"Color\",\"value\":{\"r\":0.1,\"g\":0.5,\"b\":0.7,\"a\":1}}]}");
            Add(entries, "physics_materials/0.json", MResEntryType.PhysicsMaterial, "{\"name\":\"Bounce\",\"dynamicFriction\":0.2,\"staticFriction\":0.3,\"bounciness\":0.5,\"frictionCombine\":2,\"bounceCombine\":3}");
            entries.Add(Tuple.Create("meshes/0.meshbin", MResEntryType.Mesh, Mesh()));
            entries.Add(Tuple.Create("textures/0.png", MResEntryType.Texture, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=")));
            if (lua != null) Add(entries, "scripts/main.lua", MResEntryType.Lua, lua);
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write(new byte[28]); var offsets = new List<ulong>();
                foreach (var e in entries) { offsets.Add((ulong)stream.Position); w.Write(e.Item3); }
                ulong toc = (ulong)stream.Position;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i]; byte[] path = Encoding.UTF8.GetBytes(e.Item1);
                    w.Write((uint)path.Length); w.Write(path); w.Write((byte)e.Item2); w.Write((byte)0); w.Write((ushort)0);
                    w.Write(offsets[i]); w.Write((ulong)e.Item3.Length);
                }
                ulong size = (ulong)stream.Position - toc;
                stream.Position = 0; w.Write(0x5345524Du); w.Write((ushort)1); w.Write((ushort)0);
                w.Write((uint)entries.Count); w.Write(toc); w.Write(size); w.Flush(); return stream.ToArray();
            }
        }
        private static void Add(List<Tuple<string, MResEntryType, byte[]>> e, string path, MResEntryType type, string value)
            => e.Add(Tuple.Create(path, type, Encoding.UTF8.GetBytes(value)));
        private static byte[] Mesh()
        {
            using (var s = new MemoryStream())
            using (var w = new BinaryWriter(s))
            {
                w.Write(0x48534D4Du); w.Write((ushort)1); w.Write((byte)0); w.Write((byte)31); w.Write(4u); w.Write(2u);
                foreach (float f in new float[] { 0, 0, 0, 2, 0, 2 }) w.Write(f);
                foreach (float f in new float[] { -2, 0, -2, -2, 0, 2, 2, 0, 2, 2, 0, -2 }) w.Write(f);
                for (int i = 0; i < 4; i++) { w.Write(0f); w.Write(1f); w.Write(0f); }
                for (int i = 0; i < 4; i++) { w.Write(1f); w.Write(0f); w.Write(0f); w.Write(1f); }
                for (int channel = 0; channel < 2; channel++)
                    foreach (float f in new float[] { 0, 0, 0, 1, 1, 1, 1, 0 }) w.Write(f);
                for (int i = 0; i < 16; i++) w.Write(1f);
                w.Write(0); w.Write(3u); w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2);
                w.Write(0); w.Write(3u); w.Write((ushort)0); w.Write((ushort)2); w.Write((ushort)3);
                return s.ToArray();
            }
        }
    }
}
