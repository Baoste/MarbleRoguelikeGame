using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Checks the actual colliders under the scene's explicitly selected obstacle roots.</summary>
    internal sealed class DevicePlacementObstacles
    {
        private Collider[] overlaps = new Collider[32];

        internal bool IsClear(PhysicsScene scene, Transform[] roots, Vector3 position, float radius)
        {
            if (!scene.IsValid()) return false;
            if (roots == null || roots.Length == 0) return true;
            Physics.SyncTransforms();
            int count;
            do
            {
                count = scene.OverlapSphere(position, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                Array.Resize(ref overlaps, checked(overlaps.Length * 2));
            } while (true);
            for (int i = 0; i < count; i++)
                foreach (Transform root in roots)
                    if (root != null && overlaps[i].transform.IsChildOf(root)) return false;
            return true;
        }
    }
}
