using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS
{
    internal enum FloatingTextMerge : byte { None, Source, Target }

    internal sealed class FloatingTextConfig
    {
        internal const string ResourcePath = "FloatingText/Config";
        internal const int MaxCapacity = 8192;

        internal readonly bool Enabled, ShowNeutralPins, MergeEnabled;
        internal readonly int Capacity, PendingCapacity, MaxCharacters, Layer, SortingOrder;
        internal readonly float MergeSeconds;
        internal readonly string SortingLayer, MaterialPath, GlyphsPath;
        internal readonly Dictionary<int, FloatingTextStyle> Styles = new Dictionary<int, FloatingTextStyle>();
        private readonly HashSet<(FloatingTextSource, int)> unmergedSources = new HashSet<(FloatingTextSource, int)>();
        private readonly FloatingTextStyle[] defaults = new FloatingTextStyle[(int)FloatingTextType.PinContact + 1];
        private readonly bool[] mergeTypes = new bool[(int)FloatingTextType.PinContact + 1];
        private readonly FloatingTextStyle pinStyle, highMultiplierStyle;
        private readonly double highMultiplierThreshold;

        internal static FloatingTextConfig Load(TextAsset source = null)
        {
            source = source != null ? source : Resources.Load<TextAsset>(ResourcePath);
            if (source == null) throw new ArgumentException("Missing Resources/" + ResourcePath + ".json.");
            return new FloatingTextConfig(ConfigJson.Read(source.text, source.name));
        }

        private FloatingTextConfig(ConfigFields fields)
        {
            Enabled = fields.Bool("enabled", true);
            if (!Enabled) return;
            Capacity = Integer(fields, "capacity", 1, MaxCapacity, 1024);
            PendingCapacity = Integer(fields, "pendingCapacity", 1, MaxCapacity * 4, 4096);
            MaxCharacters = Integer(fields, "maxCharacters", 8, 24, 12);
            ShowNeutralPins = fields.Bool("showNeutralPins", false);
            MergeEnabled = fields.Bool("mergeEnabled", true);
            MergeSeconds = (float)Number(fields, "mergeSeconds", 0, 2, 0.1);
            Layer = Integer(fields, "layer", 0, 31, 0);
            SortingLayer = fields.String("sortingLayer", "Default", optional: true);
            SortingOrder = Integer(fields, "sortingOrder", short.MinValue, short.MaxValue, 100);
            MaterialPath = fields.String("material", "FloatingText/Numbers", optional: true);
            GlyphsPath = fields.String("glyphs", "FloatingText/Glyphs", optional: true);
            highMultiplierThreshold = Number(fields, "highMultiplierThreshold", 1, 1000, 5);

            foreach (var entry in fields.Array("styles"))
            {
                var styleFields = ConfigFields.From(entry, fields.Context + "/styles");
                var style = new FloatingTextStyle(styleFields, MaxCharacters);
                if (!Styles.TryAdd(style.Id, style)) throw styleFields.Error("duplicate style id " + style.Id);
            }
            var routes = fields.Object("defaultStyles");
            for (int i = 0; i < defaults.Length; i++) defaults[i] = ReadStyle(routes, ((FloatingTextType)i).ToString());
            pinStyle = ReadStyle(routes, "PinMultiplier");
            highMultiplierStyle = ReadStyle(routes, "HighMultiplier");

            if (fields.Has("mergeTypes"))
            {
                foreach (var value in fields.Array("mergeTypes"))
                {
                    if (!(value is string name) || !Enum.TryParse(name, out FloatingTextType type) || type.ToString() != name ||
                        (type != FloatingTextType.Additive && type != FloatingTextType.Currency && type != FloatingTextType.Blood))
                        throw fields.Error("mergeTypes");
                    mergeTypes[(int)type] = true;
                }
            }
            else mergeTypes[(int)FloatingTextType.Additive] = mergeTypes[(int)FloatingTextType.Currency] = mergeTypes[(int)FloatingTextType.Blood] = true;

            foreach (var entry in fields.Array("unmergedSources", optional: true))
            {
                var source = ConfigFields.From(entry, fields.Context + "/unmergedSources");
                unmergedSources.Add((Named<FloatingTextSource>(source, "source"), Integer(source, "id", 1, int.MaxValue)));
            }
        }

        private FloatingTextStyle ReadStyle(ConfigFields fields, string key) =>
            Styles.TryGetValue(fields.Int(key), out var style) ? style : throw fields.Error(key + " style id");

        internal FloatingTextStyle Resolve(in FloatingTextEvent text)
        {
            if (text.StyleId != 0 && Styles.TryGetValue(text.StyleId, out var style)) return style;
            if (text.Type == FloatingTextType.Multiplier)
            {
                if (text.Value >= highMultiplierThreshold) return highMultiplierStyle;
                if (text.Source == FloatingTextSource.Pin) return pinStyle;
            }
            return defaults[(int)text.Type];
        }

        internal bool CanMerge(in FloatingTextEvent text) => MergeEnabled && MergeSeconds > 0 && text.SourceId > 0 &&
            mergeTypes[(int)text.Type] && !unmergedSources.Contains((text.Source, text.SourceId));

        internal static double Number(ConfigFields fields, string key, double minimum, double maximum, double? fallback = null)
        {
            double value = fields.Number(key, fallback.GetValueOrDefault(), fallback.HasValue);
            if (value < minimum || value > maximum) throw fields.Error(key);
            return value;
        }

        internal static int Integer(ConfigFields fields, string key, int minimum, int maximum, int? fallback = null)
        {
            double value = Number(fields, key, minimum, maximum, fallback);
            if (value != Math.Truncate(value)) throw fields.Error(key);
            return (int)value;
        }

        internal static T Named<T>(ConfigFields fields, string key, T? fallback = null) where T : struct, Enum
        {
            if (!fields.Has(key) && fallback.HasValue) return fallback.Value;
            string name = fields.String(key);
            if (!Enum.TryParse(name, out T result) || result.ToString() != name) throw fields.Error(key);
            return result;
        }

        internal static float Element(object value, ConfigFields fields, string key, float min, float max) =>
            value is double number && number >= min && number <= max ? (float)number : throw fields.Error(key);
    }

    internal sealed class FloatingTextStyle
    {
        internal readonly int Id, Decimals, CompactDecimals;
        internal readonly double AbbreviateAt;
        internal readonly byte Priority;
        internal readonly float Lifetime, Height, InitialScale, PeakScale, RiseSpeed, HorizontalSpeed, Spacing;
        internal readonly Vector3 Offset;
        internal readonly Color32 Color;
        internal readonly FloatingTextMerge Merge;
        internal readonly bool Monospace, ShowValue;
        internal readonly string Prefix, Suffix;
        internal readonly FloatingTextCurve Scale, Alpha;

        internal FloatingTextStyle(ConfigFields fields, int maxCharacters)
        {
            Id = FloatingTextConfig.Integer(fields, "id", 1, int.MaxValue);
            Priority = (byte)FloatingTextConfig.Integer(fields, "priority", byte.MinValue, byte.MaxValue);
            Lifetime = (float)FloatingTextConfig.Number(fields, "lifetime", 0.01, 30);
            Height = (float)FloatingTextConfig.Number(fields, "height", 0.001, 10);
            InitialScale = (float)FloatingTextConfig.Number(fields, "initialScale", 0, 1, 0.65);
            PeakScale = (float)FloatingTextConfig.Number(fields, "peakScale", 1, 5, 1.35);
            RiseSpeed = (float)FloatingTextConfig.Number(fields, "riseSpeed", 0, 20, 0.6);
            HorizontalSpeed = (float)FloatingTextConfig.Number(fields, "horizontalSpeed", 0, 20, 0.22);
            Spacing = (float)FloatingTextConfig.Number(fields, "spacing", 0, 1, 0.02);
            Offset = ReadOffset(fields);
            if (!ColorUtility.TryParseHtmlString(fields.String("color"), out var color)) throw fields.Error("color");
            Color = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            Merge = FloatingTextConfig.Named<FloatingTextMerge>(fields, "merge", FloatingTextMerge.None);
            Monospace = fields.Bool("monospace", false);
            ShowValue = fields.Bool("showValue", true);
            Prefix = fields.String("prefix", string.Empty, optional: true);
            Suffix = fields.String("suffix", string.Empty, optional: true);
            if (Prefix.Length + Suffix.Length > (ShowValue ? maxCharacters - 7 : maxCharacters) ||
                (!ShowValue && Prefix.Length + Suffix.Length == 0)) throw fields.Error("prefix/suffix length");
            Decimals = FloatingTextConfig.Integer(fields, "decimals", 0, 6, 2);
            CompactDecimals = FloatingTextConfig.Integer(fields, "compactDecimals", 0, 6, 2);
            AbbreviateAt = FloatingTextConfig.Number(fields, "abbreviateAt", 0, 1e15, 10000);
            if (AbbreviateAt > 0 && AbbreviateAt < 1000) throw fields.Error("abbreviateAt");
            Scale = fields.Has("scaleCurve") ? new FloatingTextCurve(fields, "scaleCurve", -1, 1) : FloatingTextCurve.DefaultScale;
            Alpha = fields.Has("alphaCurve") ? new FloatingTextCurve(fields, "alphaCurve", 0, 1) : FloatingTextCurve.DefaultAlpha;
        }

        private static Vector3 ReadOffset(ConfigFields fields)
        {
            if (!fields.Has("offset")) return Vector3.zero;
            var values = fields.Array("offset");
            if (values.Count != 3) throw fields.Error("offset");
            return new Vector3(FloatingTextConfig.Element(values[0], fields, "offset", -10, 10),
                FloatingTextConfig.Element(values[1], fields, "offset", -10, 10),
                FloatingTextConfig.Element(values[2], fields, "offset", -10, 10));
        }
    }

    internal sealed class FloatingTextCurve
    {
        private const int Resolution = 64;
        private readonly float[] samples = new float[Resolution + 1];
        internal static readonly FloatingTextCurve DefaultScale = new FloatingTextCurve(new[]
            { new Vector2(0, -1), new Vector2(.17f, 1), new Vector2(.37f, 0), new Vector2(1, 0) });
        internal static readonly FloatingTextCurve DefaultAlpha = new FloatingTextCurve(new[]
            { new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(1, 0) });

        internal FloatingTextCurve(ConfigFields fields, string key, float min, float max) : this(Read(fields, key, min, max)) { }

        private FloatingTextCurve(Vector2[] keys)
        {
            int next = 1;
            for (int i = 0; i <= Resolution; i++)
            {
                float t = (float)i / Resolution;
                while (t > keys[next].x) next++;
                var a = keys[next - 1];
                var b = keys[next];
                samples[i] = Mathf.SmoothStep(a.y, b.y, (t - a.x) / (b.x - a.x));
            }
        }

        private static Vector2[] Read(ConfigFields fields, string key, float min, float max)
        {
            var values = fields.Array(key);
            if (values.Count < 2 || values.Count > Resolution) throw fields.Error(key);
            var keys = new Vector2[values.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                if (!(values[i] is List<object> pair) || pair.Count != 2) throw fields.Error(key);
                float t = FloatingTextConfig.Element(pair[0], fields, key, 0, 1);
                if (i > 0 && t <= keys[i - 1].x) throw fields.Error(key + " key order");
                keys[i] = new Vector2(t, FloatingTextConfig.Element(pair[1], fields, key, min, max));
            }
            if (keys[0].x != 0 || keys[keys.Length - 1].x != 1) throw fields.Error(key + " endpoints");
            return keys;
        }

        internal float Evaluate(float time)
        {
            float position = Mathf.Clamp01(time) * Resolution;
            int index = Mathf.Min((int)position, Resolution - 1);
            return Mathf.LerpUnclamped(samples[index], samples[index + 1], position - index);
        }
    }
}
