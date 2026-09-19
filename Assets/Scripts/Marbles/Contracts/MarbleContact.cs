using UnityEngine;

namespace MarblesECS
{
    public struct MarbleContact
    {
        public MarbleKey Key;
        public MarbleContactKind Kind;
        public int TargetId;
        public int Priority;
        public double Multiplier;
        public double BaseRushChance;
        public double RushChanceAdd;
        public bool RushDisabled;
        public long FlatScore;
        public Vector3 Position;
    }
}
