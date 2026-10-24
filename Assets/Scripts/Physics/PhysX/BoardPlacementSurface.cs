using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Unit-scale board frame: local X points uphill, local Y is the surface normal.</summary>
    public sealed class BoardPlacementSurface : MonoBehaviour
    {
        [Min(.1f)] public float Length = 7.979034f;
        [Min(.1f)] public float TopWidth = 5.784827f;
        [Min(.1f)] public float BottomWidth = 8.779388f;
        [Min(0)] public float EdgeMargin = .12f;
        [Min(0)] public float TopClearance = .65f;
        [Min(0)] public float BottomClearance = .70f;

        public bool ContainsWorldPoint(Vector3 worldPoint, float radius)
        {
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            float r = Mathf.Max(0, radius);
            if (p.x + r > Length * .5f - TopClearance ||
                p.x - r < -Length * .5f + BottomClearance) return false;
            // Distance to the sloping side, not just its horizontal projection.
            float slope = (TopWidth - BottomWidth) / (2 * Length);
            float halfWidth = Mathf.Lerp(BottomWidth, TopWidth, (p.x + Length * .5f) / Length) * .5f;
            return Mathf.Abs(p.z) + (r + EdgeMargin) * Mathf.Sqrt(1 + slope * slope) <= halfWidth;
        }

        private void OnDrawGizmosSelected()
        {
            var matrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.cyan;
            var a = new Vector3(-Length * .5f, 0, -BottomWidth * .5f);
            var b = new Vector3(-Length * .5f, 0, BottomWidth * .5f);
            var c = new Vector3(Length * .5f, 0, TopWidth * .5f);
            var d = new Vector3(Length * .5f, 0, -TopWidth * .5f);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
            Gizmos.matrix = matrix;
        }
    }
}
