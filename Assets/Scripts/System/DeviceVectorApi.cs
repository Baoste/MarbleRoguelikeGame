using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Pure vector calculations; these never move a ball or consume random state.</summary>
    [Preserve]
    public sealed class DeviceVectorApi
    {
        private readonly Func<SimulationContext> context;
        internal DeviceVectorApi(DeviceContactContext owner) : this(() => owner.Context) { }
        internal DeviceVectorApi(Func<SimulationContext> context) { this.context = context; }
        public Vector3 Normalize(Vector3 vector)
        {
            _ = context(); Validate(vector);
            return vector.sqrMagnitude < 1e-12f ? Vector3.zero : Unit(vector);
        }
        public Vector3 ProjectOnPlane(Vector3 vector, Vector3 normal)
        {
            _ = context(); Validate(vector); Validate(normal, true);
            return Vector3.ProjectOnPlane(vector, Unit(normal));
        }
        public Vector3 Reflect(Vector3 vector, Vector3 normal)
        {
            _ = context(); Validate(vector); Validate(normal, true);
            return Vector3.Reflect(vector, Unit(normal));
        }
        public float Dot(Vector3 a, Vector3 b)
        {
            _ = context(); Validate(a); Validate(b);
            return Vector3.Dot(a, b);
        }
        public float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            _ = context(); Validate(from, true); Validate(to, true); Validate(axis, true);
            return Vector3.SignedAngle(Unit(from), Unit(to), Unit(axis));
        }
        public Vector3 Rotate(Vector3 vector, Vector3 axis, float degrees)
        {
            _ = context(); Validate(vector); Validate(axis, true);
            if (!BallInteractionSystem.Finite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
            return Quaternion.AngleAxis(degrees % 360, Unit(axis)) * vector;
        }
        private static Vector3 Unit(Vector3 vector) => vector / Mathf.Sqrt(vector.sqrMagnitude);
        private static void Validate(Vector3 vector, bool nonzero = false)
        {
            if (!SimulationContext.IsFinite(vector) || !BallInteractionSystem.Finite(vector.sqrMagnitude) ||
                (nonzero && vector.sqrMagnitude < 1e-12f)) throw new ArgumentException("Expected a finite vector and nonzero normal/direction/axis.");
        }
    }
}
