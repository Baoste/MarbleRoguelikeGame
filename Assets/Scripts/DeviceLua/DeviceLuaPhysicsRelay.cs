using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS
{
    // Static child colliders and child rigidbodies receive their own Unity messages.
    // Forward these to the device's root behaviour; compound children with an ancestor body need no relay.
    [AddComponentMenu("")]
    internal sealed class DeviceLuaPhysicsRelay : MonoBehaviour
    {
        private DeviceLuaBehaviour target;
        internal static void Attach(IEnumerable<Transform> nodes, DeviceLuaBehaviour behaviour)
        {
            foreach (var node in nodes)
            {
                if (node == behaviour.transform) continue;
                bool body = node.GetComponent<Rigidbody>() != null;
                bool staticCollider = node.GetComponent<Collider>() != null && node.GetComponentInParent<Rigidbody>(true) == null;
                if (body || staticCollider) node.gameObject.AddComponent<DeviceLuaPhysicsRelay>().target = behaviour;
            }
        }
        private void Send(int index, GameObject other) { if (target != null) target.PhysicsEvent(index, other); }
        private void OnCollisionEnter(Collision c) => Send(3, c.gameObject);
        private void OnCollisionStay(Collision c) => Send(4, c.gameObject);
        private void OnCollisionExit(Collision c) => Send(5, c.gameObject);
        private void OnTriggerEnter(Collider c) => Send(6, c.gameObject);
        private void OnTriggerStay(Collider c) => Send(7, c.gameObject);
        private void OnTriggerExit(Collider c) => Send(8, c.gameObject);
    }
}
