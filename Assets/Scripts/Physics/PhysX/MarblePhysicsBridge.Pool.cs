using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge
    {
        private MarbleBody TakeFromPool()
        {
            while (pool.Count > 0)
            {
                var view = pool.Pop();
                if (view != null) return view;
            }
            return null;
        }

        private void ReturnToPool(MarbleBody view)
        {
            view.Bridge = null;
            view.Key = default;

            var collider = view.GetComponent<SphereCollider>();
            if (collider != null) collider.enabled = false;

            var body = view.Body;
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.detectCollisions = false;
                body.isKinematic = true;
            }

            view.gameObject.SetActive(false);
            view.gameObject.name = "Marble (Pooled)";
            pool.Push(view);
        }

    }
}
