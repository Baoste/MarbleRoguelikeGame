using System;
using UnityEngine;

namespace MarblesECS
{
    [AddComponentMenu("")]
    internal sealed class DeviceLuaBehaviour : MonoBehaviour
    {
        private static readonly string[] Events = { "Start", "Update", "FixedUpdate", "OnCollisionEnter", "OnCollisionStay",
            "OnCollisionExit", "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit" };
        private DeviceLuaScript script;
        internal DeviceMetadata Metadata => script?.Metadata;
        internal void Initialize(DeviceLuaScript value)
        {
            script = value;
            script.BindRoot(gameObject);
        }
        private void Invoke(int index, params object[] args)
        {
            if (script == null || !enabled) return;
            try { script.Invoke(Events[index], args); }
            catch (Exception e) { enabled = false; Debug.LogException(e, this); }
        }
        internal void PhysicsEvent(int index, GameObject other) => Invoke(index, DeviceLuaObject.Other(other));
        private void Start() => Invoke(0);
        private void Update() => Invoke(1, Time.deltaTime);
        private void FixedUpdate() => Invoke(2, Time.fixedDeltaTime);
        private void OnCollisionEnter(Collision c) => PhysicsEvent(3, c.gameObject);
        private void OnCollisionStay(Collision c) => PhysicsEvent(4, c.gameObject);
        private void OnCollisionExit(Collision c) => PhysicsEvent(5, c.gameObject);
        private void OnTriggerEnter(Collider c) => PhysicsEvent(6, c.gameObject);
        private void OnTriggerStay(Collider c) => PhysicsEvent(7, c.gameObject);
        private void OnTriggerExit(Collider c) => PhysicsEvent(8, c.gameObject);
        private void OnDestroy() => Release();
        internal void Release()
        {
            if (script == null) return;
            script.UnbindRoot(gameObject);
            script = null;
        }
    }
}
