namespace MRes
{
    public enum MResEntryType : byte
    {
        Manifest = 1, Prefab = 2, Mesh = 3, Material = 4, Texture = 5,
        Lua = 6, Metadata = 7, PhysicsMaterial = 8, Other = 255
    }

    public sealed class MResEntry
    {
        public string Path { get; }
        public MResEntryType Type { get; }
        public ulong Offset { get; }
        public ulong Size { get; }
        internal MResEntry(string path, MResEntryType type, ulong offset, ulong size)
        { Path = path; Type = type; Offset = offset; Size = size; }
    }

    public static class MResFormat
    {
        public const int HeaderSize = 28;
        public const int Version = 1;
        public const int MaxPackageBytes = 256 * 1024 * 1024;
        public const int MaxEntries = 65536;
        public const int MaxPathBytes = 1024;
        public const int MaxNodes = 10000;
        public const int MaxVertices = 1000000;
        public const int MaxIndices = 6000000;
        public const int MaxTextureDimension = 8192;
    }
}
