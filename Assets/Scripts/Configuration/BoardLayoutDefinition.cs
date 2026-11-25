using System;
using System.Collections.Generic;

namespace MarblesECS
{
    public sealed class BoardLayoutDefinition
    {
        public string Source { get; }
        public IReadOnlyList<BoardLayoutPin> Pins { get; }
        public IReadOnlyList<BoardLayoutZone> Zones { get; }

        internal BoardLayoutDefinition(string source, BoardLayoutPin[] pins, BoardLayoutZone[] zones)
        {
            Source = source;
            Pins = Array.AsReadOnly(pins);
            Zones = Array.AsReadOnly(zones);
        }
    }

    [Serializable]
    public struct BoardLayoutPin
    {
        public float x, y, radius;
        public float scoreMultiplier, rushChanceAdd;
    }

    [Serializable]
    public struct BoardLayoutZone
    {
        public string type;
        public float x, y, width, depth, triggerHeight;
        public float multiplier;
        public long flatScore;
        public int priority;
        public bool rushEnabled, scoreStackEnabled;

        internal MarbleContactKind Kind => type == nameof(MarbleContactKind.RandomScore)
            ? MarbleContactKind.RandomScore : MarbleContactKind.Score;
    }
}
