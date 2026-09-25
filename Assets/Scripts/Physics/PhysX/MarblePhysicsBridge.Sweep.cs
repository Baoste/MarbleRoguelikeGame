using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge
    {
        private RaycastHit[] sweepHits = new RaycastHit[32];
        private Collider[] sensorOverlaps = new Collider[32];

        /// <summary>One gameplay tick, several short PhysX steps. Events settle after every substep is collected.</summary>
        public void Simulate(float seconds)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MarblePhysicsBridge));
            if (!MarbleRules.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            float subdivisions = 1;
            RefreshContentDevices();
            foreach (var view in bodies.Values)
                if (view != null && view.Body != null)
                    subdivisions = Mathf.Max(subdivisions, view.Body.velocity.magnitude * seconds /
                        Mathf.Max(0.025f, view.Radius * 0.75f));
            int steps = Mathf.Clamp(Mathf.CeilToInt(subdivisions), 1, 32);
            for (int i = 0; i < steps; i++)
            {
                CaptureBeforeStep();
                BeforeContentSubstep(seconds / steps);
                physicsScene.Simulate(seconds / steps);
                ReportSweptZones();
                AfterContentSubstep();
            }
        }

        public void CaptureBeforeStep()
        {
            foreach (var view in bodies.Values)
                if (view != null && view.Body != null) view.PreviousPosition = view.Body.position;
        }

        public void ReportSweptZones()
        {
            foreach (var view in bodies.Values)
            {
                if (view == null || view.Body == null || !view.gameObject.activeInHierarchy) continue;
                ReportOverlaps(view, view.PreviousPosition);
                var displacement = view.Body.position - view.PreviousPosition;
                float distance = displacement.magnitude;
                if (distance > 0.00001f)
                {
                    int count;
                    do
                    {
                        count = physicsScene.SphereCast(view.PreviousPosition, view.Radius, displacement / distance,
                            sweepHits, distance, ~0, QueryTriggerInteraction.Collide);
                        if (count < sweepHits.Length) break;
                        Array.Resize(ref sweepHits, checked(sweepHits.Length * 2));
                    } while (true);
                    for (int i = 0; i < count; i++) ReportSensor(view, sweepHits[i].collider);
                }
                ReportOverlaps(view, view.Body.position);
            }
        }

        private void ReportOverlaps(MarbleBody view, Vector3 position)
        {
            int count;
            do
            {
                count = physicsScene.OverlapSphere(position, view.Radius, sensorOverlaps, ~0, QueryTriggerInteraction.Collide);
                if (count < sensorOverlaps.Length) break;
                Array.Resize(ref sensorOverlaps, checked(sensorOverlaps.Length * 2));
            } while (true);
            for (int i = 0; i < count; i++) ReportSensor(view, sensorOverlaps[i]);
        }

        private void ReportSensor(MarbleBody view, Collider collider)
        {
            if (collider == null || !collider.isTrigger) return;
            var device = collider.GetComponentInParent<MarbleDevice>();
            if (device != null) { ReportDevice(view, device); return; }
            var zone = collider.GetComponentInParent<MarbleZone>();
            if (zone != null && zone.isActiveAndEnabled && zone.IsValid)
            {
                var contact = zone.CreateContact(view.Key, view.Body.position); contact.Speed = view.Body.velocity.magnitude;
                Report(contact);
            }
        }
    }
}
