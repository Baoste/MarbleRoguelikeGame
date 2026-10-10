using System.Collections.Generic;
using UnityEngine;

namespace MRes
{
    // Static child colliders and child rigidbodies receive their own Unity messages.
    // Forward these to the package's one behaviour; compound children with an ancestor body need no relay.
    [AddComponentMenu("")]
    internal sealed class MResPhysicsRelay : MonoBehaviour
    {
        private MResRuntimeBehaviour target;
        internal static void Attach(IEnumerable<GameObject> nodes, MResRuntimeBehaviour behaviour)
        {
            foreach (var node in nodes)
            {
                if (node == behaviour.gameObject) continue;
                bool body = node.GetComponent<Rigidbody>() != null;
                bool staticCollider = node.GetComponent<Collider>() != null && node.GetComponentInParent<Rigidbody>(true) == null;
                if (body || staticCollider) node.AddComponent<MResPhysicsRelay>().target = behaviour;
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
