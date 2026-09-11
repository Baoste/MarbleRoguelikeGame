using System;

namespace MarblesECS
{
    public readonly struct MarbleKey : IEquatable<MarbleKey>
    {
        public readonly int RoundId;
        public readonly ulong Sequence;
        public MarbleKey(int roundId, ulong sequence) { RoundId = roundId; Sequence = sequence; }
        public bool Equals(MarbleKey other) { return RoundId == other.RoundId && Sequence == other.Sequence; }
        public override bool Equals(object obj) { return obj is MarbleKey other && Equals(other); }
        public override int GetHashCode() { unchecked { return (RoundId * 397) ^ Sequence.GetHashCode(); } }
        public override string ToString() { return RoundId + ":" + Sequence; }
    }
}
