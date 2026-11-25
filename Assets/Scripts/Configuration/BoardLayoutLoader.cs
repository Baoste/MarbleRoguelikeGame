using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS
{
    public static class BoardLayoutLoader
    {
        internal const string AssetFolder = "Assets/Config/Resources/BoardLayouts";
        private const string ResourceFolder = "BoardLayouts";
        private const int SchemaVersion = 1;

        [Serializable]
        private sealed class LayoutFile
        {
            public int schemaVersion = SchemaVersion;
            public BoardLayoutPin[] pins;
            public BoardLayoutZone[] zones;
        }

        public static BoardLayoutDefinition Load(string file)
        {
            string path = ResourcePath(file);
            var source = Resources.Load<TextAsset>(path);
            if (!source) throw new ArgumentException($"Board layout '{file}' was not found under {AssetFolder}.");
            return Parse(source.text, file);
        }

        public static BoardLayoutDefinition Parse(string json, string source = "Board layout")
        {
            var input = ConfigJson.Read(json, source);
            if (input.Int("schemaVersion") != SchemaVersion) throw input.Error("schemaVersion");
            var pins = ReadEntries(input, "pins", entry => new BoardLayoutPin
            {
                x = Number(entry, "x", 0, 1),
                y = Number(entry, "y", 0, 1),
                radius = Positive(entry, "radius"),
                scoreMultiplier = Number(entry, "scoreMultiplier", 0, 1000, 2),
                rushChanceAdd = Number(entry, "rushChanceAdd", 0, 1, 0)
            });
            var zones = ReadEntries(input, "zones", ReadZone);
            return new BoardLayoutDefinition(source, pins, zones);
        }

        internal static string ToJson(List<BoardLayoutPin> pins, List<BoardLayoutZone> zones) =>
            JsonUtility.ToJson(new LayoutFile { pins = pins.ToArray(), zones = zones.ToArray() }, true);

        internal static string ResourcePath(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("A board layout .json file name is required.");
            string normalized = file.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || segments[segments.Length - 1].Length <= 5 ||
                Array.Exists(segments, part => string.IsNullOrWhiteSpace(part) || part == "." || part == ".." || part.Contains(":")))
                throw new ArgumentException($"Invalid board layout file '{file}'. Use a path relative to {AssetFolder} ending in .json.");
            return ResourceFolder + "/" + normalized.Substring(0, normalized.Length - 5);
        }

        private static T[] ReadEntries<T>(ConfigFields input, string field, Func<ConfigFields, T> read)
        {
            var list = input.Array(field);
            var result = new T[list.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = read(ConfigFields.From(list[i], $"{input.Context}/{field}[{i}]"));
            return result;
        }

        private static BoardLayoutZone ReadZone(ConfigFields entry)
        {
            string type = entry.String("type");
            if (type != nameof(MarbleContactKind.Score) && type != nameof(MarbleContactKind.RandomScore)) throw entry.Error("type");
            double flatScore = entry.Number("flatScore", 0, optional: true);
            // ConfigJson reads numbers as double. Use the exact integer range of that representation.
            if (flatScore < 0 || flatScore > 9007199254740991d || flatScore != Math.Truncate(flatScore)) throw entry.Error("flatScore");
            return new BoardLayoutZone
            {
                type = type,
                x = Number(entry, "x", 0, 1),
                y = Number(entry, "y", 0, 1),
                width = Positive(entry, "width"),
                depth = Positive(entry, "depth"),
                triggerHeight = Positive(entry, "triggerHeight", 2.2f),
                multiplier = Number(entry, "multiplier", 0, 1000, 1),
                flatScore = (long)flatScore,
                priority = entry.Int("priority", 0, optional: true),
                rushEnabled = entry.Bool("rushEnabled", true),
                scoreStackEnabled = entry.Bool("scoreStackEnabled", true)
            };
        }

        private static float Number(ConfigFields entry, string key, float min, float max, float? fallback = null)
        {
            double value = entry.Number(key, fallback.GetValueOrDefault(), optional: fallback.HasValue);
            if (value < min || value > max) throw entry.Error(key);
            return (float)value;
        }

        private static float Positive(ConfigFields entry, string key, float? fallback = null)
        {
            float value = Number(entry, key, 0, float.MaxValue, fallback);
            if (value <= 0) throw entry.Error(key);
            return value;
        }
    }
}
