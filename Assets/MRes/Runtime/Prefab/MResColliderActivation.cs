using System.Collections.Generic;
using UnityEngine;

namespace MRes
{
    // PhysX shape-only settings (notably contactOffset) are lost when assigned to an
    // inactive/disabled collider. Keep the authored values until its shape exists.
    [AddComponentMenu("")]
    [DefaultExecutionOrder(10000)]
    internal sealed class MResColliderActivation : MonoBehaviour
    {
        private sealed class State
        {
            internal Collider Collider;
            internal MResComponentData Data;
            internal bool WasEnabled;
        }
        private readonly List<State> states = new List<State>();
        internal void Track(Collider collider, MResComponentData data)
            => states.Add(new State { Collider = collider, Data = data });
        internal void Apply()
        {
            foreach (var state in states)
            {
                if (state.Collider == null) continue;
                state.WasEnabled = state.Collider.enabled && state.Collider.gameObject.activeInHierarchy;
                if (state.WasEnabled) Restore(state);
            }
        }
        private static void Restore(State state)
        {
            state.Collider.contactOffset = state.Data.ContactOffset;
#if UNITY_2022_2_OR_NEWER
            state.Collider.providesContacts = state.Data.ProvidesContacts;
#endif
#if UNITY_2021_2_OR_NEWER
            state.Collider.hasModifiableContacts = state.Data.ModifiableContacts;
#endif
        }
        private void OnEnable() => Apply();
        private void OnDisable() { foreach (var state in states) state.WasEnabled = false; }
        private void FixedUpdate()
        {
            foreach (var state in states)
            {
                if (state.Collider == null) continue;
                bool active = state.Collider.enabled;
                if (active && !state.WasEnabled) Restore(state);
                state.WasEnabled = active;
            }
        }
    }
}
