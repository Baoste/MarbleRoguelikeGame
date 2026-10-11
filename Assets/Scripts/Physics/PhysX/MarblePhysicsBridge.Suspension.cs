using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge : IMarblePhysicsSuspension
    {
        public bool TrySetSuspended(MarbleKey key, bool suspended)
        {
            if (!bodies.TryGetValue(key, out var view) || view == null || view.Body == null) return false;
            if (view.Suspended == suspended) return true;
            var body = view.Body;
            if (suspended)
            {
                if (body.isKinematic) return false;
                view.SuspendedSpin = body.angularVelocity;
                view.SuspendedCollisionMode = body.collisionDetectionMode;
                view.SuspendedDetectCollisions = body.detectCollisions;
                body.velocity = body.angularVelocity = Vector3.zero;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.detectCollisions = false;
                body.isKinematic = true;
            }
            else
            {
                body.isKinematic = false;
                body.detectCollisions = view.SuspendedDetectCollisions;
                body.collisionDetectionMode = view.SuspendedCollisionMode;
                body.angularVelocity = view.SuspendedSpin;
                body.WakeUp();
            }
            view.Suspended = suspended;
            view.PreviousPosition = body.position;
            return true;
        }
    }
}
