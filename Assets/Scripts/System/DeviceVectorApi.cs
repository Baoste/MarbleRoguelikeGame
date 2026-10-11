using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Pure vector calculations; these never move a ball or consume random state.</summary>
    [Preserve]
    public sealed class DeviceVectorApi
    {
        private readonly DeviceContactContext owner;
        internal DeviceVectorApi(DeviceContactContext owner) { this.owner = owner; }
        public Vector3 Normalize(Vector3 vector)
        {
            _ = owner.Context; Validate(vector);
            return vector.sqrMagnitude < 1e-12f ? Vector3.zero : Unit(vector);
        }
        public Vector3 ProjectOnPlane(Vector3 vector, Vector3 normal)
        {
            _ = owner.Context; Validate(vector); Validate(normal, true);
            return Vector3.ProjectOnPlane(vector, Unit(normal));
        }
        public Vector3 Reflect(Vector3 vector, Vector3 normal)
        {
            _ = owner.Context; Validate(vector); Validate(normal, true);
            return Vector3.Reflect(vector, Unit(normal));
        }
        public float Dot(Vector3 a, Vector3 b)
        {
            _ = owner.Context; Validate(a); Validate(b);
            return Vector3.Dot(a, b);
        }
        public float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            _ = owner.Context; Validate(from, true); Validate(to, true); Validate(axis, true);
            return Vector3.SignedAngle(Unit(from), Unit(to), Unit(axis));
        }
        private static Vector3 Unit(Vector3 vector) => vector / Mathf.Sqrt(vector.sqrMagnitude);
        private static void Validate(Vector3 vector, bool nonzero = false)
        {
            if (!SimulationContext.IsFinite(vector) || !BallInteractionSystem.Finite(vector.sqrMagnitude) ||
                (nonzero && vector.sqrMagnitude < 1e-12f)) throw new ArgumentException("Expected a finite vector and nonzero normal/direction/axis.");
        }
    }
}
