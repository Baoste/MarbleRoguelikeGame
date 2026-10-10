using System;
using System.Collections.Generic;
using System.IO;

namespace MResPacker
{
    public enum MResEntryType : byte
    { Manifest = 1, Prefab = 2, Mesh = 3, Material = 4, Texture = 5, Lua = 6, Metadata = 7, PhysicsMaterial = 8, Other = 255 }
    public sealed class MResEntryData
    {
        public readonly string Path;
        public readonly MResEntryType Type;
        public readonly byte[] Bytes;
        public MResEntryData(string path, MResEntryType type, byte[] bytes) { Path = path; Type = type; Bytes = bytes; }
    }
    public static class MResWriter
    {
        public const int HeaderSize = 28, MaxPackageBytes = 256 * 1024 * 1024;
        public static void Write(string outputPath, IReadOnlyList<MResEntryData> entries)
        {
            if (string.IsNullOrWhiteSpace(outputPath) || !outputPath.EndsWith(".mres", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output must be a .mres file.");
            string final = System.IO.Path.GetFullPath(outputPath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(final));
            string temporary = final + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    WriteStream(stream, entries);
                    stream.Flush(true);
                }
                MResStructureValidator.ValidateFile(temporary);
                // Preserve an existing successful package until its complete replacement validates.
                if (File.Exists(final)) File.Replace(temporary, final, null); else File.Move(temporary, final);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static long Estimate(IReadOnlyList<MResEntryData> entries)
        {
            long total = HeaderSize;
            foreach (var e in entries) total += 24L + MResJson.Utf8.GetByteCount(e.Path) + e.Bytes.LongLength;
            return total;
        }
        private static void WriteStream(Stream stream, IReadOnlyList<MResEntryData> entries)
        {
            if (entries == null || entries.Count == 0 || entries.Count > 65536) throw new ArgumentException("Invalid entry count.");
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                MResStructureValidator.ValidatePath(e.Path);
                if (!paths.Add(e.Path) || e.Bytes == null) throw new ArgumentException("Duplicate/empty entry: " + e.Path);
            }
            if (Estimate(entries) > MaxPackageBytes) throw new ArgumentException("Package exceeds 256 MiB.");
            using (var w = new BinaryWriter(stream, MResJson.Utf8, true))
            {
                w.Write(new byte[HeaderSize]); var offsets = new ulong[entries.Count];
                for (int i = 0; i < entries.Count; i++) { offsets[i] = (ulong)stream.Position; w.Write(entries[i].Bytes); }
                ulong toc = (ulong)stream.Position;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i]; var path = MResJson.Utf8.GetBytes(e.Path);
                    w.Write((uint)path.Length); w.Write(path); w.Write((byte)e.Type); w.Write((byte)0); w.Write((ushort)0);
                    w.Write(offsets[i]); w.Write((ulong)e.Bytes.LongLength);
                }
                ulong size = (ulong)stream.Position - toc;
                stream.Position = 0; w.Write(0x5345524Du); w.Write((ushort)1); w.Write((ushort)0);
                w.Write((uint)entries.Count); w.Write(toc); w.Write(size); w.Flush();
            }
        }
    }
}
