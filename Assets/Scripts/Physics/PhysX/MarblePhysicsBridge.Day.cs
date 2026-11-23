using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge : IMarbleDayPhysics
    {
        public IMarbleDayPhysicsCheckpoint CaptureDayCheckpoint()
        {
            if (disposed) throw new ObjectDisposedException(nameof(MarblePhysicsBridge));
            if (bodies.Count != 0) throw new InvalidOperationException("A Day checkpoint cannot contain a live Rigidbody.");
            return new DayPhysicsCheckpoint(this, new List<Collider>(brokenPins.Keys).ToArray());
        }

        public void ResetDayPhysics()
        {
            if (disposed) throw new ObjectDisposedException(nameof(MarblePhysicsBridge));
            RetireBodies();
            ClearTransientVisuals();
            RestorePins();
            contentDevices.Clear();
            orderedFieldBodies.Clear();
            Array.Clear(overlap, 0, overlap.Length);
            Array.Clear(pinHits, 0, pinHits.Length);
            Array.Clear(sweepHits, 0, sweepHits.Length);
            Array.Clear(sensorOverlaps, 0, sensorOverlaps.Length);
            Array.Clear(devicePickHits, 0, devicePickHits.Length);
        }

        private sealed class DayPhysicsCheckpoint : IMarbleDayPhysicsCheckpoint
        {
            private readonly MarblePhysicsBridge bridge;
            private readonly Collider[] brokenPins;

            internal DayPhysicsCheckpoint(MarblePhysicsBridge bridge, Collider[] brokenPins)
            {
                this.bridge = bridge;
                this.brokenPins = brokenPins;
            }

            public void Restore()
            {
                bridge.ResetDayPhysics();
                foreach (var pin in brokenPins)
                    if (pin != null) bridge.BreakPin(pin);
                Physics.SyncTransforms();
            }
        }
    }
}
