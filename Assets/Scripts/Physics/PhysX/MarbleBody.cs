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

        private void OnTriggerEnter(Collider other) { Report(other); }
        // 覆盖出生时已经重叠的区域；ECS 按球和区域去重，Stay 不会重复计分。
        private void OnTriggerStay(Collider other) { Report(other); }
        private void OnCollisionEnter(Collision collision) { ReportPin(collision.collider); }
        private void OnCollisionStay(Collision collision) { ReportPin(collision.collider); }

        private void ReportPin(Collider other)
        {
            if (Bridge == null || Body == null) return;
            var pin = other.GetComponentInParent<MarblePin>();
            if (pin != null && pin.isActiveAndEnabled && pin.IsValid)
                Bridge.Report(pin.CreateContact(Key, Body.position));
        }

        private void Report(Collider other)
        {
            if (Bridge == null || Body == null || !other.isTrigger) return;
            var zone = other.GetComponentInParent<MarbleZone>();
            if (zone != null && zone.isActiveAndEnabled && zone.IsValid)
                Bridge.Report(zone.CreateContact(Key, Body.position));
        }

        private void OnDestroy()
        {
            if (Bridge != null) Bridge.NotifyDestroyed(Key, this);
            if (RuntimeMaterial != null) Destroy(RuntimeMaterial);
        }
    }
}
