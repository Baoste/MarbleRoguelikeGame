using Unity.Entities;

namespace MarblesECS
{
    // These components are the gameplay source of truth. Rigidbody owns motion.
    internal struct RoundData : IComponentData
    {
        public int RoundId;
        public long Tick;
        public double Time;
        public double DrainStartedAt;
        public long Score;
        public long PendingScore;
        public long GamblingPayout;
        public bool SettlementComplete;
        public bool GamblingWon;
        public long TargetScore;
        public int ActiveMarbleCount;
        public byte Phase;
        public uint RandomState;
        public long RushEndTick;
    }
}
