using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.PhysX
{
    /// <summary>PhysX 是位置/速度的唯一所有者。物理回调只输出数据，不结算玩法。</summary>
    public sealed partial class MarblePhysicsBridge : IMarblePhysics, IMarblePhysicsStateProvider, IDisposable
    {
        private readonly Scene scene;
        private readonly PhysicsScene physicsScene;
        private readonly GameObject prefab;
        private readonly PhysicMaterial material;
        private readonly Action<MarbleContact> onContact;
        private readonly Dictionary<MarbleKey, MarbleBody> bodies = new Dictionary<MarbleKey, MarbleBody>();
        private readonly Stack<MarbleBody> pool = new Stack<MarbleBody>();
        private readonly Collider[] overlap = new Collider[1];
        private bool disposed;

        public int ActiveBodyCount => bodies.Count;
        public int PooledBodyCount => pool.Count;

        public MarblePhysicsBridge(Scene scene, GameObject prefab, PhysicMaterial material, Action<MarbleContact> onContact)
        {
            this.scene = scene;
            physicsScene = scene.GetPhysicsScene();
            this.prefab = prefab;
            this.material = material;
            this.onContact = onContact;
            if (!physicsScene.IsValid()) throw new ArgumentException("需要有效的本地3D物理场景。");
            if (prefab != null)
            {
                var sphere = prefab.GetComponent<SphereCollider>();
                var colliders = prefab.GetComponentsInChildren<Collider>(true);
                if (sphere == null || sphere.isTrigger || sphere.center != Vector3.zero ||
                    Mathf.Abs(sphere.radius - 0.5f) > 0.0001f || colliders.Length != 1)
                    throw new ArgumentException("MarblePrefab 需在根上有唯一的非Trigger SphereCollider，center=0，radius=0.5；网格直径为1。");
            }
        }

        public bool TryGetPosition(MarbleKey key, out Vector3 position)
        {
            position = default;
            if (disposed || !bodies.TryGetValue(key, out var view) || view == null ||
                !view.gameObject.activeInHierarchy || view.Body == null) return false;
            position = view.Body.position;
            return true;
        }

        public bool TryGetState(MarbleKey key, out MarblePhysicsState state)
        {
            state = default;
            if (!TryGetPosition(key, out var position)) return false;
            var body = bodies[key].Body;
            state = new MarblePhysicsState { Position = position, Rotation = body.rotation.normalized,
                LinearVelocity = body.velocity, AngularVelocity = body.angularVelocity, BodyHandle = body.GetInstanceID() };
            return true;
        }

        internal void Report(MarbleContact contact)
        {
            if (!disposed && bodies.ContainsKey(contact.Key)) onContact?.Invoke(contact);
        }

        internal void NotifyDestroyed(MarbleKey key, MarbleBody view)
        {
            if (bodies.TryGetValue(key, out var current) && ReferenceEquals(current, view)) bodies.Remove(key);
        }

        public void Remove(MarbleKey key)
        {
            if (!bodies.TryGetValue(key, out var view)) return;
            bodies.Remove(key);
            if (view == null) return;
            ReturnToPool(view);
        }

        public void Dispose()
        {
            if (disposed) return;
            foreach (var key in new List<MarbleKey>(bodies.Keys)) Remove(key);
            disposed = true;
            while (pool.Count > 0)
            {
                var view = pool.Pop();
                if (view != null) UnityEngine.Object.Destroy(view.gameObject);
            }
        }

        private static bool Finite(Vector3 value)
        {
            return MarbleRules.IsFinite(value.x) && MarbleRules.IsFinite(value.y) && MarbleRules.IsFinite(value.z);
        }
    }
}
