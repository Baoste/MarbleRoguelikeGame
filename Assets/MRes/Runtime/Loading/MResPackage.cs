using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MRes
{
    /// <summary>Validated, in-memory package. Read never constructs Unity objects.</summary>
    public sealed class MResPackage
    {
        private readonly byte[] bytes;
        private readonly Dictionary<string, MResEntry> entries;
        private MResMetadata metadata;
        public IReadOnlyDictionary<string, MResEntry> Entries { get; }
        public string Name { get; private set; }
        public MResMetadata Metadata => metadata.Copy();
        public string LuaSource { get; private set; }
        internal MResPrefabData Prefab;
        internal readonly Dictionary<string, MResMeshData> Meshes = new Dictionary<string, MResMeshData>(StringComparer.Ordinal);
        internal readonly Dictionary<string, MResTextureData> Textures = new Dictionary<string, MResTextureData>(StringComparer.Ordinal);
        internal readonly Dictionary<string, MResMaterialData> Materials = new Dictionary<string, MResMaterialData>(StringComparer.Ordinal);
        internal readonly Dictionary<string, MResPhysicsMaterialData> PhysicsMaterials = new Dictionary<string, MResPhysicsMaterialData>(StringComparer.Ordinal);

        internal MResPackage(byte[] bytes, Dictionary<string, MResEntry> entries)
        {
            this.bytes = bytes; this.entries = entries;
            Entries = new ReadOnlyDictionary<string, MResEntry>(entries);
        }
        public byte[] GetBytes(string path)
        {
            if (!entries.TryGetValue(path, out var e)) throw new MResMissingResourceException("Missing entry: " + path);
            var result = new byte[(int)e.Size];
            Buffer.BlockCopy(bytes, (int)e.Offset, result, 0, result.Length); return result;
        }
        internal MResFields Json(string path) => new MResFields(MResJson.Parse(Text(path), path), path);
        private string Text(string path)
        {
            string value = MResReader.Utf8.GetString(GetBytes(path));
            return value.Length > 0 && value[0] == '\uFEFF' ? value.Substring(1) : value;
        }
        internal void Require(string path, MResEntryType type)
        {
            MResReader.ValidatePath(path);
            if (!entries.TryGetValue(path, out var e)) throw new MResMissingResourceException("Missing " + type + ": " + path);
            if (e.Type != type) throw new MResFormatException("Wrong entry type for " + path + "; expected " + type);
        }
        private List<string> Paths(MResFields manifest, string key, MResEntryType type)
        {
            var result = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object v in manifest.Array(key, true))
            {
                if (!(v is string path) || !seen.Add(path)) throw manifest.Error(key);
                Require(path, type); result.Add(path);
            }
            return result;
        }
        internal void Validate()
        {
            string manifestPath = null;
            foreach (var entry in entries.Values)
                if (entry.Type == MResEntryType.Manifest)
                {
                    if (manifestPath != null) throw new MResFormatException("Expected exactly one manifest.");
                    manifestPath = entry.Path;
                }
            if (manifestPath == null) throw new MResMissingResourceException("Missing manifest.");
            var manifest = Json(manifestPath);
            if (manifest.String("format") != "mres" || manifest.Int("version") != 1) throw manifest.Error("format/version");
            Name = manifest.String("packageName");
            string prefabPath = manifest.String("prefab"), metadataPath = manifest.String("metadata");
            Require(prefabPath, MResEntryType.Prefab); Require(metadataPath, MResEntryType.Metadata);
            var m = Json(metadataPath);
            string type = m.String("type");
            if (type != "medicine" && type != "device") throw m.Error("type (medicine/device only)");
            metadata = new MResMetadata { name = m.String("name"), description = m.String("description"),
                value = m.Float("value"), type = type == "medicine" ? MResItemType.Medicine : MResItemType.Device,
                purchasePrice = m.Float("purchasePrice", 5, true), durability = m.Int("durability", 20, true),
                rarity = m.String("rarity", "common", true) };
            if (metadata.value < 0 || metadata.purchasePrice < 1 || (metadata.durability != -1 && (metadata.durability < 1 || metadata.durability > 10000)) ||
                (metadata.rarity != "common" && metadata.rarity != "rare" && metadata.rarity != "epic" && metadata.rarity != "legendary"))
                throw m.Error("value/purchasePrice/rarity/durability");
            string lua = manifest.String("lua", optional: true);
            if (lua != null) { Require(lua, MResEntryType.Lua); LuaSource = Text(lua); }
            MResFields settings = manifest.Has("textureSettings") ? manifest.Object("textureSettings") : null;
            foreach (string path in Paths(manifest, "textures", MResEntryType.Texture))
                Textures.Add(path, MResTextureReader.Parse(GetBytes(path), path, settings != null && settings.Has(path) ? settings.Object(path) : null));
            if (settings != null)
                foreach (string path in settings.Fields.Keys)
                    if (!Textures.ContainsKey(path)) throw new MResMissingResourceException("Texture settings reference undeclared texture: " + path);
            foreach (string path in Paths(manifest, "meshes", MResEntryType.Mesh)) Meshes.Add(path, MResMeshReader.Parse(GetBytes(path), path));
            foreach (string path in Paths(manifest, "physicsMaterials", MResEntryType.PhysicsMaterial))
                PhysicsMaterials.Add(path, MResPhysicsMaterialReader.Parse(Json(path)));
            foreach (string path in Paths(manifest, "materials", MResEntryType.Material))
                Materials.Add(path, MResMaterialReader.Parse(Json(path), this));
            Prefab = MResPrefabData.Parse(Json(prefabPath), this);
        }
        internal static void Reference<T>(Dictionary<string, T> resources, string path, string context, bool nullable = false)
        {
            if (nullable && path == null) return;
            if (path == null || !resources.ContainsKey(path)) throw new MResMissingResourceException(context + ": undeclared resource " + path);
        }
    }
}
