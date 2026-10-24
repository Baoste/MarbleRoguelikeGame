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
        public bool SettlementComplete;
        public long TargetScore;
        public int ActiveMarbleCount;
        public byte Phase;
        // The run's only random stream: starters, shop, device effects and settlement share it.
        public uint RandomState;
        public long RushEndTick;
        public double RushRemaining, RushMeter, FractionalScore, FractionalPendingScore;
        public double RushDuration;
        public long ScoreStackCount;
        public double ScoreStackExpiresAt;
        public double ScoreStackWindowSeconds;
        public int WheelBallCount, WheelLastMultiplier;
        public long WheelDrawCount, WheelLastScoreChange;
        public double WheelExpiresAt, WheelWindowSeconds, WheelLastPool;
        public bool CashoutRequested;
    }
}
