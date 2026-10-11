using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS.PhysX
{
    [Preserve]
    public sealed class DeviceFieldContext
    {
        private MResDeviceBody device;
        private readonly bool magnetic;
        private readonly float response, magneticRadius, range, strength, distance, forwardOffset, radius, deltaTime;
        private readonly Vector3 position;
        private readonly Vector3 toward;
        private readonly Func<bool> tryUse;
        private bool usageGranted;
        internal Vector3 Force { get; private set; }
        private void RequireOpen()
        { if (device == null) throw new InvalidOperationException("The Lua field callback has ended."); }
        internal void Close() { device = null; }
        public bool Magnetic { get { RequireOpen(); return magnetic; } }
        public float Response { get { RequireOpen(); return response; } }
        public float MagneticRadius { get { RequireOpen(); return magneticRadius; } }
        public float Range { get { RequireOpen(); return range; } }
        public float Strength { get { RequireOpen(); return strength; } }
        public float Distance { get { RequireOpen(); return distance; } }
        public float ForwardOffset { get { RequireOpen(); return forwardOffset; } }
        public Vector3 Forward { get { RequireOpen(); return device.Forward; } }
        public Vector3 Up { get { RequireOpen(); return device.Up; } }
        public float DeltaTime { get { RequireOpen(); return deltaTime; } }
        internal DeviceFieldContext(MResDeviceBody device, MarbleBody ball, float deltaTime, Func<bool> tryUse = null)
        {
            this.tryUse = tryUse;
            this.device = device; this.deltaTime = deltaTime;
            magnetic = ball.Settings.Magnetic; response = ball.Settings.MagnetResponse; magneticRadius = ball.Settings.MagneticRadius;
            range = device.Range; strength = device.Strength; radius = ball.Radius; position = ball.Body.position;
            var offset = position - device.transform.position;
            var delta = Vector3.ProjectOnPlane(-offset, device.Up);
            distance = delta.magnitude; toward = delta.normalized; forwardOffset = Vector3.Dot(offset, device.Forward);
        }
        /// <summary>One use for this ball's continuous stay. Leaving the Lua-selected region permits another use.</summary>
        public bool TryUse()
        { RequireOpen(); return usageGranted || (usageGranted = tryUse?.Invoke() ?? true); }
        /// <summary>Tests the current ball sphere against a named child box, including its actual pose and scale.</summary>
        public bool OverlapsBox(string nodePath)
        {
            RequireOpen();
            var box = device.FindFieldBox(nodePath);
            if (box == null || !box.enabled || !box.gameObject.activeInHierarchy) return false;
            return (box.ClosestPoint(position) - position).sqrMagnitude <= radius * radius;
        }
        /// <summary>Adds a world-space force in newtons for this substep; the bridge integrates dt and mass once.</summary>
        public bool TryAddForce(Vector3 worldForce)
        {
            RequireOpen();
            if (!SimulationContext.IsFinite(worldForce) || !MarbleRules.IsFinite(worldForce.sqrMagnitude)) return false;
            var sum = Force + worldForce;
            if (!SimulationContext.IsFinite(sum) || !MarbleRules.IsFinite(sum.sqrMagnitude)) return false;
            Force = sum;
            return true;
        }
        public void AddRadialForce(float magnitude)
        { RequireOpen(); if (MarbleRules.IsFinite(magnitude)) TryAddForce(toward * magnitude); }
    }
}
