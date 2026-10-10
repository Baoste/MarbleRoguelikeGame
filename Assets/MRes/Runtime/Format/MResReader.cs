using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MRes
{
    internal static class MResReader
    {
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static MResPackage Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
            // Starts at the caller's current position; accepts non-seekable streams and leaves them open.
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[81920];
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (memory.Length + count > MResFormat.MaxPackageBytes) throw new MResFormatException("Package exceeds 256 MiB.");
                    memory.Write(buffer, 0, count);
                }
                return ReadOwned(memory.ToArray());
            }
        }

        internal static MResPackage Read(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length > MResFormat.MaxPackageBytes) throw new MResFormatException("Package exceeds 256 MiB.");
            return ReadOwned((byte[])bytes.Clone());
        }

        private static MResPackage ReadOwned(byte[] bytes)
        {
            try
            {
                if (bytes.Length < MResFormat.HeaderSize) throw new MResFormatException("Truncated MRES header.");
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadUInt32() != 0x5345524D) throw new MResFormatException("Expected MRES magic.");
                    if (reader.ReadUInt16() != 1) throw new MResFormatException("Unsupported MRES version.");
                    if (reader.ReadUInt16() != 0) throw new MResFormatException("Unsupported MRES flags.");
                    uint count = reader.ReadUInt32();
                    ulong toc = reader.ReadUInt64(), tocSize = reader.ReadUInt64();
                    Range(toc, tocSize, (ulong)bytes.Length, "TOC");
                    if (count == 0 || count > MResFormat.MaxEntries || tocSize < (ulong)count * 25)
                        throw new MResFormatException("Invalid entry count/TOC size.");
                    var entries = new Dictionary<string, MResEntry>(StringComparer.Ordinal);
                    var ranges = new List<MResEntry>();
                    stream.Position = (long)toc;
                    for (uint i = 0; i < count; i++)
                    {
                        if ((ulong)stream.Position + 4 > toc + tocSize) throw new MResFormatException("Truncated TOC.");
                        uint length = reader.ReadUInt32();
                        if (length == 0 || length > MResFormat.MaxPathBytes || (ulong)stream.Position + length + 20 > toc + tocSize)
                            throw new MResFormatException("Invalid TOC path length.");
                        string path = Utf8.GetString(reader.ReadBytes((int)length));
                        ValidatePath(path);
                        byte type = reader.ReadByte();
                        if (!((type >= 1 && type <= 8) || type == 255)) throw new MResFormatException("Unknown entry type: " + path);
                        if (reader.ReadByte() != 0 || reader.ReadUInt16() != 0) throw new MResFormatException("Compression/reserved must be zero: " + path);
                        ulong offset = reader.ReadUInt64(), size = reader.ReadUInt64();
                        Range(offset, size, (ulong)bytes.Length, path);
                        if (size > 0 && offset < toc + tocSize && toc < offset + size) throw new MResFormatException("Entry overlaps TOC: " + path);
                        if (entries.ContainsKey(path)) throw new MResFormatException("Duplicate path: " + path);
                        var entry = new MResEntry(path, (MResEntryType)type, offset, size);
                        entries.Add(path, entry);
                        if (size > 0) ranges.Add(entry);
                    }
                    if ((ulong)stream.Position != toc + tocSize) throw new MResFormatException("TOC size does not match entries.");
                    ranges.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                    for (int i = 1; i < ranges.Count; i++)
                        if (ranges[i].Offset < ranges[i - 1].Offset + ranges[i - 1].Size)
                            throw new MResFormatException("Overlapping entries: " + ranges[i].Path);
                    var package = new MResPackage(bytes, entries);
                    package.Validate();
                    return package;
                }
            }
            catch (DecoderFallbackException e) { throw new MResFormatException("Invalid UTF-8.", e); }
            catch (EndOfStreamException e) { throw new MResFormatException("Truncated MRES file.", e); }
        }

        private static void Range(ulong offset, ulong size, ulong length, string context)
        {
            if (offset < MResFormat.HeaderSize || offset > length || size > length - offset)
                throw new MResFormatException("Range outside package: " + context);
        }

        internal static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > MResFormat.MaxPathBytes || path[0] == '/' ||
                path.IndexOfAny(new[] { '\\', ':', '\0', '?', '*', '"', '<', '>', '|' }) >= 0)
                throw new MResFormatException("Invalid virtual path: " + path);
            foreach (char c in path) if (char.IsControl(c) || char.IsSurrogate(c)) throw new MResFormatException("Invalid virtual path: " + path);
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.Trim() != part || part.EndsWith(".", StringComparison.Ordinal))
                    throw new MResFormatException("Invalid virtual path: " + path);
        }
    }
}
