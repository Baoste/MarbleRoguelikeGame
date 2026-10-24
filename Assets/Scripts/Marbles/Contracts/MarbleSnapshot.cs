using UnityEngine;

namespace MarblesECS
{
    public struct MarbleSnapshot
    {
        public int RoundId;
        public long Tick;
        public RoundPhase Phase;
        public float Blood;
        public long Score;
        public long TargetScore;
        public int ActiveMarbles;
        public double TimeSeconds;
        public double RushSecondsRemaining;
        public double RushMeter, RushMaximum;
        public double ScoreStackBonus, ScoreStackSecondsRemaining;
        public double ShotsPerSecond;
        public float Flow;
        public float NextMarbleRestitution;
    }
}
