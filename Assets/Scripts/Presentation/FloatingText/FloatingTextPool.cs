using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS
{
    internal readonly struct FloatingTextMergeKey : IEquatable<FloatingTextMergeKey>
    {
        private readonly FloatingTextSource source;
        private readonly int sourceId, styleId;
        private readonly FloatingTextType type;
        private readonly MarbleKey target;

        internal FloatingTextMergeKey(in FloatingTextEvent text, FloatingTextStyle style)
        {
            source = text.Source;
            sourceId = text.SourceId;
            type = text.Type;
            styleId = style.Id;
            target = style.Merge == FloatingTextMerge.Target ? text.Target : default;
        }

        public bool Equals(FloatingTextMergeKey other) => source == other.source && sourceId == other.sourceId &&
            type == other.type && styleId == other.styleId && target.Equals(other.target);
        public override bool Equals(object obj) => obj is FloatingTextMergeKey other && Equals(other);
        public override int GetHashCode() => unchecked((((sourceId * 397 ^ styleId) * 397 ^ (int)source) * 397 ^ (int)type) *
            397 ^ target.GetHashCode());
    }

    internal struct FloatingTextData
    {
        internal Vector3 Position;
        internal double SpawnTime, LastEventTime;
        internal float HorizontalSpeed, Width;
        internal int GlyphCount;
        internal FloatingTextStyle Style;
        internal FloatingTextType Type;
        internal FloatingTextValue Value;
        internal FloatingTextMergeKey MergeKey;
        internal bool TrackedForMerge;
    }

    internal sealed class FloatingTextPool
    {
        private readonly FloatingTextConfig config;
        private readonly FloatingTextAtlas atlas;
        private readonly Dictionary<FloatingTextMergeKey, int> mergeSlots;
        private uint visualRandom = 0x9e3779b9;

        internal readonly FloatingTextSlots Slots;
        internal readonly FloatingTextData[] Items;
        internal readonly FloatingTextGlyph[] Glyphs;
        internal long Spawned { get; private set; }
        internal long Merged { get; private set; }
        internal long Dropped { get; private set; }
        internal long Evicted { get; private set; }

        internal FloatingTextPool(FloatingTextConfig config, FloatingTextAtlas atlas, int capacity)
        {
            this.config = config;
            this.atlas = atlas;
            Slots = new FloatingTextSlots(capacity);
            Items = new FloatingTextData[capacity];
            Glyphs = new FloatingTextGlyph[capacity * config.MaxCharacters];
            mergeSlots = new Dictionary<FloatingTextMergeKey, int>(capacity);
        }

        internal void Recycle(double now, bool scoresOnly)
        {
            for (int slot = Slots.First; slot >= 0;)
            {
                int next = Slots.Next(slot);
                ref var item = ref Items[slot];
                if (now - item.SpawnTime >= item.Style.Lifetime || (scoresOnly && item.Type != FloatingTextType.Score))
                    Remove(slot);
                slot = next;
            }
        }

        internal void Accept(in FloatingTextRequest request, double now)
        {
            var style = request.Style;
            var text = request.Event;
            bool merge = style.Merge != FloatingTextMerge.None && config.CanMerge(text) &&
                (style.Merge != FloatingTextMerge.Target || text.Target.Sequence != 0);
            var key = merge ? new FloatingTextMergeKey(text, style) : default;
            Span<char> characters = stackalloc char[config.MaxCharacters];
            if (merge && mergeSlots.TryGetValue(key, out int match) && TryMerge(match, request, now, characters)) return;

            int replace = -1;
            if (Slots.Count == Slots.Capacity)
            {
                replace = Slots.OldestBelow(request.Priority);
                if (replace < 0) { Dropped++; return; }
            }
            int count = FloatingTextFormatter.Write(text.Amount, text.Type, style, characters);
            if (count == 0) { Dropped++; return; }
            if (replace >= 0) { Remove(replace); Evicted++; }

            int slot = Slots.Add(request.Priority);
            Items[slot] = new FloatingTextData
            {
                Position = text.Position, SpawnTime = now, LastEventTime = now, Style = style,
                Type = text.Type, Value = text.Amount, HorizontalSpeed = NextDirection() * style.HorizontalSpeed,
                TrackedForMerge = merge, MergeKey = key
            };
            CacheGlyphs(slot, characters.Slice(0, count));
            if (merge) mergeSlots[key] = slot;
            Spawned++;
        }

        private bool TryMerge(int slot, in FloatingTextRequest request, double now, Span<char> characters)
        {
            ref var item = ref Items[slot];
            if (now - item.LastEventTime > config.MergeSeconds || !item.Value.TryAdd(request.Event.Amount, out var sum)) return false;
            int count = FloatingTextFormatter.Write(sum, item.Type, item.Style, characters);
            if (count == 0) return false;
            item.Value = sum;
            item.LastEventTime = now;
            CacheGlyphs(slot, characters.Slice(0, count));
            Slots.RaisePriority(slot, request.Priority);
            Merged++;
            return true;
        }

        private void CacheGlyphs(int slot, ReadOnlySpan<char> characters)
        {
            ref var item = ref Items[slot];
            float width = (characters.Length - 1) * item.Style.Spacing;
            int first = slot * config.MaxCharacters;
            for (int i = 0; i < characters.Length; i++)
            {
                var glyph = atlas[characters[i]];
                Glyphs[first + i] = glyph;
                width += item.Style.Monospace ? atlas.WidestGlyph : glyph.Width;
            }
            item.Width = width;
            item.GlyphCount = characters.Length;
        }

        private float NextDirection()
        {
            visualRandom ^= visualRandom << 13;
            visualRandom ^= visualRandom >> 17;
            visualRandom ^= visualRandom << 5;
            return (visualRandom >> 8) * (2f / 16777215f) - 1f;
        }

        private void Remove(int slot)
        {
            ref var item = ref Items[slot];
            if (item.TrackedForMerge && mergeSlots.TryGetValue(item.MergeKey, out int current) && current == slot)
                mergeSlots.Remove(item.MergeKey);
            Slots.Remove(slot);
        }

        internal void Clear()
        {
            Slots.Clear();
            mergeSlots.Clear();
        }
    }
}
