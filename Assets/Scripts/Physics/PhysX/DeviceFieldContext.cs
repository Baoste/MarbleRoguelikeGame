using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS.PhysX
{
    [Preserve]
    public sealed class DeviceFieldContext
    {
        public bool Magnetic { get; }
        public float Response { get; }
        public float MagneticRadius { get; }
        public float Range { get; }
        public float Strength { get; }
        public float Distance { get; }
        public float ForwardOffset { get; }
        private readonly Vector3 toward;
        internal Vector3 Force { get; private set; }
        internal DeviceFieldContext(MResDeviceBody device, MarbleBody ball)
        {
            Magnetic = ball.Settings.Magnetic; Response = ball.Settings.MagnetResponse; MagneticRadius = ball.Settings.MagneticRadius;
            Range = device.Range; Strength = device.Strength;
            var offset = ball.Body.position - device.transform.position;
            var delta = Vector3.ProjectOnPlane(-offset, device.Up);
            Distance = delta.magnitude; toward = delta.normalized; ForwardOffset = Vector3.Dot(offset, device.Forward);
        }
        public void AddRadialForce(float magnitude)
        { if (MarbleRules.IsFinite(magnitude)) Force += toward * magnitude; }
    }
}
