namespace MarblesECS
{
    public struct GamblingWheelSnapshot
    {
        public double PoolValue;
        public int BallCount;
        public double SecondsRemaining, WindowSeconds;
        public long DrawCount;
        public int LastMultiplier;
        public double LastPool;
        public long LastScoreChange;
    }
}
