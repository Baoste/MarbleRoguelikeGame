using System;
using UnityEngine;

namespace MRes
{
    /// <summary>Reads authored placement geometry without spawning objects or executing Lua.</summary>
    public static class MResPlacementSphere
    {
        // Result is relative to the placed root pose, including its authored scale, excluding its authored pose.
        public static bool TryRead(MResPackage package, out Vector3 center, out float radius)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            center = default; radius = 0; MResNodeData owner = null; MResComponentData sphere = null;
            foreach (var node in package.Prefab.Nodes)
                foreach (var component in node.Components)
                    if (component.PlacementOnly)
                    {
                        if (sphere != null) throw new MResFormatException("Only one placement SphereCollider is allowed.");
                        owner = node; sphere = component;
                    }
            if (sphere == null) return false;
            var matrix = Matrix4x4.identity;
            for (var node = owner; node != null; node = node.Parent < 0 ? null : package.Prefab.Nodes.Find(n => n.Id == node.Parent))
                matrix = (node.Parent < 0 ? Matrix4x4.Scale(node.Scale)
                    : Matrix4x4.TRS(node.Position, node.Rotation, node.Scale)) * matrix;
            center = matrix.MultiplyPoint3x4(sphere.Center);
            float scale = Mathf.Max(matrix.MultiplyVector(Vector3.right).magnitude,
                matrix.MultiplyVector(Vector3.up).magnitude, matrix.MultiplyVector(Vector3.forward).magnitude);
            radius = sphere.Radius * scale;
            if (!Finite(center.x) || !Finite(center.y) || !Finite(center.z) || !Finite(radius) || radius <= 0)
                throw new MResFormatException("Placement sphere has invalid or zero-size geometry.");
            return true;
        }
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
