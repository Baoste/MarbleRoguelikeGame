using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MResPacker
{
    /// <summary>Writer read-back checks only; never constructs Unity objects or executes Lua.</summary>
    public static class MResStructureValidator
    {
        private sealed class Entry { internal string Path; internal byte Type; internal ulong Offset, Size; }
        public static void ValidateFile(string path)
        {
            if (new FileInfo(path).Length > MResWriter.MaxPackageBytes) throw new FormatException("Package exceeds 256 MiB.");
            Validate(File.ReadAllBytes(path));
        }
        public static void Validate(byte[] bytes)
        {
            if (bytes == null || bytes.Length < MResWriter.HeaderSize || bytes.Length > MResWriter.MaxPackageBytes)
                throw new FormatException("Invalid package length.");
            using (var s = new MemoryStream(bytes, false))
            using (var r = new BinaryReader(s, MResJson.Utf8))
            {
                if (r.ReadUInt32() != 0x5345524D || r.ReadUInt16() != 1 || r.ReadUInt16() != 0) throw new FormatException("Invalid MRES header.");
                uint count = r.ReadUInt32(); ulong toc = r.ReadUInt64(), tocSize = r.ReadUInt64(), length = (ulong)s.Length;
                Range(toc, tocSize, length);
                if (count == 0 || count > 65536 || tocSize < count * 25ul || toc + tocSize != length) throw new FormatException("Invalid TOC/count.");
                var entries = new Dictionary<string, Entry>(StringComparer.Ordinal); s.Position = (long)toc;
                for (uint i = 0; i < count; i++)
                {
                    if ((ulong)s.Position + 4 > length) throw new FormatException("Truncated TOC.");
                    uint n = r.ReadUInt32();
                    if (n == 0 || n > 1024 || (ulong)s.Position + n + 20 > length) throw new FormatException("Invalid path length.");
                    string path = MResJson.Utf8.GetString(r.ReadBytes((int)n)); ValidatePath(path);
                    byte type = r.ReadByte();
                    if ((type < 1 || type > 8) && type != 255) throw new FormatException("Unknown entry type.");
                    if (r.ReadByte() != 0 || r.ReadUInt16() != 0) throw new FormatException("Compression/reserved must be zero.");
                    ulong offset = r.ReadUInt64(), size = r.ReadUInt64(); Range(offset, size, toc);
                    if (entries.ContainsKey(path)) throw new FormatException("Duplicate path " + path);
                    entries.Add(path, new Entry { Path = path, Type = type, Offset = offset, Size = size });
                }
                if ((ulong)s.Position != length) throw new FormatException("TOC size mismatch.");
                var ranges = entries.Values.Where(e => e.Size > 0).OrderBy(e => e.Offset).ToArray();
                for (int i = 1; i < ranges.Length; i++)
                    if (ranges[i].Offset < ranges[i - 1].Offset + ranges[i - 1].Size) throw new FormatException("Overlapping entries.");
                CheckReferences(bytes, entries);
            }
        }
        private static void Range(ulong offset, ulong size, ulong end)
        { if (offset < MResWriter.HeaderSize || offset > end || size > end - offset) throw new FormatException("Entry/TOC range outside data."); }
        public static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || MResJson.Utf8.GetByteCount(path) > 1024 ||
                path.IndexOfAny(new[] { '\\', ':', '\0', '?', '*', '"', '<', '>', '|' }) >= 0)
                throw new FormatException("Invalid virtual path " + path);
            foreach (char c in path) if (char.IsControl(c) || char.IsSurrogate(c)) throw new FormatException("Invalid virtual path " + path);
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.Trim() != part || part.EndsWith(".", StringComparison.Ordinal))
                    throw new FormatException("Invalid virtual path " + path);
        }
        private static Dictionary<string, object> Json(byte[] bytes, Entry e)
        {
            var copy = new byte[(int)e.Size]; Buffer.BlockCopy(bytes, (int)e.Offset, copy, 0, copy.Length);
            return MResJson.Parse(copy) as Dictionary<string, object> ?? throw new FormatException("Expected JSON object: " + e.Path);
        }
        private static Entry Ref(Dictionary<string, Entry> entries, object path, byte type)
        {
            if (!(path is string name) || !entries.TryGetValue(name, out var e) || e.Type != type)
                throw new FormatException("Missing/wrong-type reference: " + path);
            return e;
        }
        private static void CheckReferences(byte[] bytes, Dictionary<string, Entry> entries)
        {
            var manifests = entries.Values.Where(e => e.Type == 1).ToArray();
            if (manifests.Length != 1) throw new FormatException("Expected one manifest.");
            var m = Json(bytes, manifests[0]);
            if ((string)m["format"] != "mres" || (double)m["version"] != 1) throw new FormatException("Invalid manifest format.");
            var prefab = Json(bytes, Ref(entries, m["prefab"], 2));
            var metadata = Json(bytes, Ref(entries, m["metadata"], 7));
            if (metadata.Keys.Any(k => !new[] { "name", "description", "value", "type", "purchasePrice", "rarity", "durability" }.Contains(k)))
                throw new FormatException("Unknown metadata field.");
            double durability = metadata.TryGetValue("durability", out var d) ? (double)d : 20;
            if (durability != Math.Truncate(durability) || (durability != -1 && (durability < 1 || durability > 10000))) throw new FormatException("Invalid durability.");
            new MResMetadataData { name = (string)metadata["name"], description = (string)metadata["description"],
                value = (float)(double)metadata["value"], type = (string)metadata["type"],
                purchasePrice = metadata.TryGetValue("purchasePrice", out var purchaseNumber) ? (float)(double)purchaseNumber : 5,
                rarity = metadata.TryGetValue("rarity", out var rarityName) ? (string)rarityName : "common", durability = (int)durability }.Validate();
            if (m.TryGetValue("lua", out var lua) && lua != null)
            {
                var e = Ref(entries, lua, 6); MResJson.Utf8.GetString(bytes, (int)e.Offset, (int)e.Size);
            }
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in new[] { ("meshes", (byte)3), ("materials", (byte)4), ("textures", (byte)5), ("physicsMaterials", (byte)8) })
                if (m.TryGetValue(pair.Item1, out var list))
                    foreach (var path in (List<object>)list)
                    { var e = Ref(entries, path, pair.Item2); if (!declared.Add(e.Path)) throw new FormatException("Duplicate manifest resource."); }
            Action<object, byte> resource = (path, type) =>
            { if (path != null && (!declared.Contains(Ref(entries, path, type).Path))) throw new FormatException("Undeclared resource " + path); };
            if (m.TryGetValue("textureSettings", out var textureSettings))
                foreach (var path in ((Dictionary<string, object>)textureSettings).Keys) resource(path, 5);
            var ids = new Dictionary<int, int>(); int roots = 0;
            foreach (var item in (List<object>)prefab["nodes"])
            {
                var node = (Dictionary<string, object>)item; int id = (int)(double)node["id"], parent = (int)(double)node["parentId"];
                if (id < 0 || ids.ContainsKey(id)) throw new FormatException("Invalid/duplicate node ID.");
                ids.Add(id, parent); if (parent == -1) roots++;
                foreach (var component in (List<object>)node["components"])
                {
                    var c = (Dictionary<string, object>)component;
                    switch ((string)c["type"])
                    {
                        case "MeshFilter": resource(c["mesh"], 3); break;
                        case "MeshRenderer": foreach (var path in (List<object>)c["materials"]) resource(path, 4); break;
                        case "MeshCollider": if (c["mesh"] == null) throw new FormatException("Missing collider mesh."); resource(c["mesh"], 3); goto case "BoxCollider";
                        case "BoxCollider": case "SphereCollider": case "CapsuleCollider": resource(c["physicsMaterial"], 8); break;
                        case "Rigidbody": break;
                        default: throw new FormatException("Unsupported component " + c["type"]);
                    }
                }
            }
            if (roots != 1 || ids.Count > 10000) throw new FormatException("Expected one root, at most 10000 nodes.");
            foreach (int id in ids.Keys)
            {
                var seen = new HashSet<int>(); int next = id;
                while (next != -1) { if (!seen.Add(next) || !ids.TryGetValue(next, out next)) throw new FormatException("Invalid hierarchy."); }
            }
            foreach (var e in entries.Values.Where(e => e.Type == 4))
                foreach (var item in (List<object>)Json(bytes, e)["properties"])
                { var p = (Dictionary<string, object>)item; if ((string)p["type"] == "Texture") resource(p["value"], 5); }
        }
    }
}
