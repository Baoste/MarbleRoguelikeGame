using System;
using UnityEngine;

namespace MRes
{
    [AddComponentMenu("")]
    internal sealed class MResRuntimeBehaviour : MonoBehaviour
    {
        private static readonly string[] Events = { "Start", "Update", "FixedUpdate", "OnCollisionEnter", "OnCollisionStay",
            "OnCollisionExit", "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit" };
        private MResLuaScript script;
        private bool ownsScript;
        internal void Initialize(string source, MResMetadata metadata, string name, MResLuaScript shared = null)
        {
            ownsScript = shared == null;
            script = shared ?? new MResLuaScript(source, metadata, name, gameObject);
            script.BindRoot(gameObject);
        }
        private void Invoke(int index, params object[] args)
        {
            if (script == null || !enabled) return;
            try { script.Invoke(Events[index], args); }
            catch (Exception e) { enabled = false; Debug.LogException(e, this); }
        }
        internal void PhysicsEvent(int index, GameObject other) => Invoke(index, MResLuaObject.Other(other));
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
            if (ownsScript) script.Dispose(); else script.UnbindRoot(gameObject);
            script = null;
        }
    }
}
