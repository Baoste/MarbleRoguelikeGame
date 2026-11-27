using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MarblesECS
{
    public sealed class DeviceResourceCatalog
    {
        internal const string PlacementVolumeName = "PlacementVolume";
        private static DeviceResourceCatalog current;
        private readonly SortedDictionary<uint, DeviceResource> resources;

        private DeviceResourceCatalog(SortedDictionary<uint, DeviceResource> resources) => this.resources = resources;

        public static DeviceResourceCatalog Current => Application.isPlaying && current != null
            ? current
            : ForContent(LoadContentAsset());

        internal static TextAsset LoadContentAsset()
        {
            var asset = Resources.Load<TextAsset>("GameContent");
            if (asset == null) throw new ArgumentException("Resources/GameContent.json is required.");
            return asset;
        }

        internal static DeviceResourceCatalog ForContent(TextAsset asset) =>
            Application.isPlaying ? current ??= Load(asset.text) : Load(asset.text);

        internal static DeviceResourceCatalog Load(string contentJson)
        {
            var content = ConfigJson.Read(contentJson, "GameContent.json");
            var ids = content.Array("Devices");
            var resources = new SortedDictionary<uint, DeviceResource>();
            for (int i = 0; i < ids.Count; i++)
            {
                if (!(ids[i] is double value) || value < 1 || value > uint.MaxValue || value != Math.Truncate(value))
                    throw content.Error($"Devices[{i}] (expected a positive integer ID)");
                uint id = (uint)value;
                if (resources.ContainsKey(id)) throw new ArgumentException($"GameContent.json contains duplicate device ID {id}.");
                resources.Add(id, ReadDevice(id));
            }
            return new DeviceResourceCatalog(resources);
        }

        private static DeviceResource ReadDevice(uint id)
        {
            string folder = "Devices/" + id.ToString(CultureInfo.InvariantCulture);
            try
            {
                var manifest = ConfigJson.Read(LoadAsset<TextAsset>(folder + "/manifest").text, folder + "/manifest.json");
                if (manifest.Int("version") != 1) throw manifest.Error("version");
                string name = manifest.String("name");
                var metadataFile = ReadEntry<TextAsset>(folder, manifest, "metadata", ".json");
                var metadata = DeviceMetadata.Read(metadataFile.text, folder + "/" + manifest.String("metadata"));
                var definition = JsonUtility.FromJson<DeviceDefinition>(ReadEntry<TextAsset>(folder, manifest, "device", ".json").text);
                if (definition == null || definition.Id != id)
                    throw new ArgumentException($"device.json must define Id {id}.");
                definition.Name = metadata.name;
                definition.Description = metadata.description;
                definition.BiomassScrapPrice = metadata.biomassScrapPrice;
                definition.Durability = metadata.durability;
                definition.Rarity = metadata.Rarity;
                definition.Validate();

                var prefab = ReadEntry<GameObject>(folder, manifest, "prefab", ".prefab");
                var icon = manifest.Has("icon") ? ReadEntry<Texture2D>(folder, manifest, "icon", ".png") : null;
                ReadPlacementSphere(prefab, definition);
                string lua = ReadEntry<TextAsset>(folder, manifest, "lua", ".lua").text;
                if (string.IsNullOrWhiteSpace(lua)) throw new ArgumentException("Lua script is empty.");
                return new DeviceResource(definition, prefab, metadata, lua, name, icon);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Invalid device {id} at Resources/{folder}: {error.Message}", error);
            }
        }

        private static T ReadEntry<T>(string folder, ConfigFields manifest, string key, string extension) where T : UnityEngine.Object
        {
            string file = manifest.String(key);
            if (file.Contains('/') || file.Contains('\\') || !file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw manifest.Error(key);
            string name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") throw manifest.Error(key);
            return LoadAsset<T>(folder + "/" + name);
        }

        private static T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = Resources.Load<T>(path);
            if (asset == null) throw new ArgumentException($"Missing {typeof(T).Name} at Resources/{path}.");
            return asset;
        }

        private static void ReadPlacementSphere(GameObject prefab, DeviceDefinition definition)
        {
            var root = prefab.transform;
            var volume = root.Find(PlacementVolumeName);
            var sphere = volume != null ? volume.GetComponent<SphereCollider>() : null;
            if (sphere == null || volume.GetComponents<Collider>().Length != 1)
                throw new ArgumentException("Prefab requires a PlacementVolume child with one SphereCollider.");

            // Keep the authored scale while measuring relative to the pose assigned during placement.
            var matrix = Matrix4x4.Scale(root.localScale) * Matrix4x4.TRS(volume.localPosition, volume.localRotation, volume.localScale);
            var center = matrix.MultiplyPoint3x4(sphere.center);
            float scale = Mathf.Max(matrix.MultiplyVector(Vector3.right).magnitude,
                matrix.MultiplyVector(Vector3.up).magnitude, matrix.MultiplyVector(Vector3.forward).magnitude);
            definition.SetPlacementSphere(center.x, center.y, center.z, sphere.radius * scale);
        }

        public DeviceResource Get(uint id)
        {
            if (!resources.TryGetValue(id, out var resource)) throw new ArgumentException($"Device ID {id} is not listed in GameContent.json.");
            return resource;
        }

        public DeviceDefinition[] Definitions()
        {
            var definitions = new DeviceDefinition[resources.Count];
            int i = 0;
            foreach (var resource in resources.Values) definitions[i++] = resource.Definition.Copy();
            return definitions;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => current = null;
    }

    public sealed class DeviceResource
    {
        private readonly DeviceMetadata metadata;
        private readonly string name;
        public DeviceMetadata Metadata => metadata.Copy();
        public DeviceDefinition Definition { get; }
        public GameObject Prefab { get; }
        public Texture2D Icon { get; }
        public string LuaSource { get; }

        internal DeviceResource(DeviceDefinition definition, GameObject prefab, DeviceMetadata metadata, string lua, string name, Texture2D icon = null)
        {
            Definition = definition;
            Prefab = prefab;
            this.metadata = metadata;
            LuaSource = lua;
            this.name = name;
            Icon = icon;
        }

        internal DeviceLuaScript CreateScript(string source = null) => new DeviceLuaScript(source ?? LuaSource, Metadata, name);
    }
}
