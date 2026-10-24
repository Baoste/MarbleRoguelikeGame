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
        public double RushChanceAdd;
        public bool RushDisabled;
        public bool ScoreStackEnabled;
        public long FlatScore;
        public Vector3 Position;
        public long LinkKey;
        public float Speed;
        // Solid-contact snapshot, captured before gameplay reads the post-solver body velocity.
        public bool HasImpact;
        public Vector3 Normal, IncomingVelocity;
    }
}
