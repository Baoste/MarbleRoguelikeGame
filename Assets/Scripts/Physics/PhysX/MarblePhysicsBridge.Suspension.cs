using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge : IMarblePhysicsSuspension, IMarbleHeldVisibility
    {
        public bool TrySetHeldVisible(MarbleKey key, bool visible)
        {
            if (!bodies.TryGetValue(key, out var view) || view == null || !view.Suspended) return false;
            if (visible) RestoreHeldVisibility(view);
            else
            {
                if (view.HeldRendererStates == null) view.HeldRendererStates = new System.Collections.Generic.Dictionary<Renderer, bool>();
                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                {
                    if (!view.HeldRendererStates.ContainsKey(renderer)) view.HeldRendererStates.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
            return true;
        }

        private static void RestoreHeldVisibility(MarbleBody view)
        {
            if (view.HeldRendererStates == null) return;
            foreach (var state in view.HeldRendererStates)
                if (state.Key != null) state.Key.forceRenderingOff = state.Value;
            view.HeldRendererStates.Clear();
        }

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
                RestoreHeldVisibility(view);
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
