using UnityEngine;

namespace MarblesECS.PhysX
{
    [DisallowMultipleComponent]
    public sealed class MarbleBody : MonoBehaviour
    {
        internal MarblePhysicsBridge Bridge;
        internal MarbleKey Key;
        internal Rigidbody Body;
        internal PhysicMaterial RuntimeMaterial;
        internal Vector3 PreviousPosition;
        internal float Radius;
        internal bool HasSettings;
        internal MarblePhysicalSettings Settings;
        internal Vector3 BeforeVelocity;
        internal uint PhysicsRandom;
        internal readonly System.Collections.Generic.HashSet<int> InsideDevices = new System.Collections.Generic.HashSet<int>();
        internal readonly System.Collections.Generic.HashSet<int> StepDevices = new System.Collections.Generic.HashSet<int>();
        internal readonly System.Collections.Generic.HashSet<int> PierceAttempts = new System.Collections.Generic.HashSet<int>();
        internal readonly System.Collections.Generic.List<Collider> IgnoredPins = new System.Collections.Generic.List<Collider>();
        private MarblePinHitText pinHitText;

        private void OnTriggerEnter(Collider other) { Report(other); }
        // 覆盖出生时已经重叠的区域；ECS 按球和区域去重，Stay 不会重复计分。
        private void OnTriggerStay(Collider other) { Report(other); }
        private void OnCollisionEnter(Collision collision) { ReportPin(collision); }
        private void OnCollisionStay(Collision collision) { }

        internal void PrepareForSpawn()
        {
            foreach (var pin in IgnoredPins) if (pin != null) Physics.IgnoreCollision(GetComponent<SphereCollider>(), pin, false);
            IgnoredPins.Clear(); PierceAttempts.Clear(); InsideDevices.Clear(); StepDevices.Clear();
            PhysicsRandom = unchecked((uint)Key.Sequence * 747796405u + (uint)Key.RoundId);
            if (pinHitText == null)
                pinHitText = GetComponentInChildren<MarblePinHitText>(true);
            pinHitText?.ResetForSpawn();
        }

        private void ReportPin(Collision collision)
        {
            if (Bridge == null || Body == null) return;
            if (HasSettings && collision.contactCount > 0 &&
                (collision.rigidbody == null || collision.rigidbody.isKinematic))
            {
                var contact = collision.GetContact(0);
                Vector3 normal = contact.normal;
                Vector3 surfaceVelocity = collision.rigidbody != null ? collision.rigidbody.GetPointVelocity(contact.point) : Vector3.zero;
                float incoming = Mathf.Max(0, -Vector3.Dot(BeforeVelocity - surfaceVelocity, normal));
                if (incoming > 0)
                {
                    // Static/kinematic surface materials must not override BALL_BOUNCE (including zero).
                    float outgoing = Vector3.Dot(Body.velocity - surfaceVelocity, normal);
                    Body.velocity = Vector3.ClampMagnitude(Body.velocity + normal * (incoming * Settings.Bounce - outgoing), Settings.MaxSpeed);
                }
            }
            var pin = collision.collider.GetComponentInParent<MarblePin>();
            if (pin != null && pin.isActiveAndEnabled && pin.IsValid)
            {
                Vector3 position = collision.contactCount > 0 ? collision.GetContact(0).point : Body.position;
                Bridge.Report(pin.CreateContact(Key, position));
                if (HasSettings && pin.ScoreMultiplier == 1 && pin.RushChanceAdd == 0)
                    Bridge.Report(new MarbleContact { Key = Key, Kind = MarbleContactKind.Pin, TargetId = pin.TargetId, Multiplier = 1, Position = position, Speed = Body.velocity.magnitude });
                pinHitText?.Play(pin, position);
            }
        }

        private void Report(Collider other)
        {
            if (Bridge == null || Body == null || !other.isTrigger) return;
            var device = other.GetComponentInParent<MarbleDevice>();
            if (device != null) { Bridge.ReportDevice(this, device); return; }
            var zone = other.GetComponentInParent<MarbleZone>();
            if (zone != null && zone.isActiveAndEnabled && zone.IsValid)
            {
                var contact = zone.CreateContact(Key, Body.position); contact.Speed = Body.velocity.magnitude;
                Bridge.Report(contact);
            }
        }

        private void OnDestroy()
        {
            if (Bridge != null) Bridge.NotifyDestroyed(Key, this);
            if (RuntimeMaterial != null) Destroy(RuntimeMaterial);
        }
    }
}
