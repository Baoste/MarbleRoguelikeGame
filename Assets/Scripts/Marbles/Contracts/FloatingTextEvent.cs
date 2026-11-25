using System;
using UnityEngine;

namespace MarblesECS
{
    public enum FloatingTextType : byte { Additive, Multiplier, Score, Critical, Custom, Currency, Blood, PinContact }
    public enum FloatingTextSource : byte { Other, Pin, Device, Zone }

    /// <summary>A presentation event from a committed gameplay operation. Positions are in world space.</summary>
    public readonly struct FloatingTextEvent
    {
        public readonly Vector3 Position;
        public readonly FloatingTextSource Source;
        public readonly int SourceId;
        public readonly MarbleKey Target;
        public readonly FloatingTextType Type;
        public readonly int StyleId;
        public readonly byte? Priority;
        internal readonly FloatingTextValue Amount;

        public FloatingTextEvent(Vector3 position, FloatingTextSource source, int sourceId, FloatingTextType type,
            double value, MarbleKey target = default, int styleId = 0, byte? priority = null)
            : this(position, source, sourceId, type, new FloatingTextValue(value), target, styleId, priority) { }

        public static FloatingTextEvent FromInteger(Vector3 position, FloatingTextSource source, int sourceId,
            FloatingTextType type, long value, MarbleKey target = default, int styleId = 0, byte? priority = null) =>
            new FloatingTextEvent(position, source, sourceId, type, new FloatingTextValue(value), target, styleId, priority);

        private FloatingTextEvent(Vector3 position, FloatingTextSource source, int sourceId, FloatingTextType type,
            FloatingTextValue amount, MarbleKey target, int styleId, byte? priority)
        {
            Position = position;
            Source = source;
            SourceId = sourceId;
            Target = target;
            Type = type;
            Amount = amount;
            StyleId = styleId;
            Priority = priority;
        }

        public double Value => Amount.Number;
        public long? ExactValue => Amount.Integer;
    }

    internal readonly struct FloatingTextValue
    {
        internal readonly double Number;
        internal readonly long? Integer;

        internal FloatingTextValue(double value) { Number = value; Integer = null; }
        internal FloatingTextValue(long value) { Number = value; Integer = value; }

        internal bool TryAdd(FloatingTextValue other, out FloatingTextValue sum)
        {
            sum = default;
            if (Integer.HasValue && other.Integer.HasValue)
            {
                long a = Integer.Value, b = other.Integer.Value;
                if ((b > 0 && a > long.MaxValue - b) || (b < 0 && a < long.MinValue - b)) return false;
                sum = new FloatingTextValue(a + b);
                return true;
            }
            double result = Number + other.Number;
            if (!MarbleRules.IsFinite(result)) return false;
            sum = new FloatingTextValue(result);
            return true;
        }
    }
}
