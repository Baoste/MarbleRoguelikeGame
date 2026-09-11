using UnityEngine;

namespace MarblesECS
{
    public struct MarbleSpawnData
    {
        public MarbleKey Key;
        public Vector3 Position;
        public Quaternion Rotation;
        public ShotQuote Quote;
        public float Speed;
        public bool HasRestitution;
        public float Restitution;
    }
}
