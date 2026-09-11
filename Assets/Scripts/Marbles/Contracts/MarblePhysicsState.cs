using UnityEngine;

namespace MarblesECS
{
    public struct MarblePhysicsState
    {
        public Vector3 Position, LinearVelocity, AngularVelocity;
        public Quaternion Rotation;
        public int BodyHandle;
    }

}
