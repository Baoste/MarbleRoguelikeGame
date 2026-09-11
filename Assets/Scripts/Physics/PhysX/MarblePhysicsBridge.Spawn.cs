using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.PhysX
{
    public sealed partial class MarblePhysicsBridge
    {
        public bool TrySpawn(MarbleSpawnData data)
        {
            if (disposed || bodies.ContainsKey(data.Key) || data.Key.RoundId <= 0 || data.Key.Sequence <= 0 ||
                !Finite(data.Position) || !MarbleRules.IsFinite(data.Speed) || data.Speed <= 0 ||
                !MarbleRules.IsFinite(data.Rotation.x) || !MarbleRules.IsFinite(data.Rotation.y) ||
                !MarbleRules.IsFinite(data.Rotation.z) || !MarbleRules.IsFinite(data.Rotation.w) ||
                ((double)data.Rotation.x * data.Rotation.x + (double)data.Rotation.y * data.Rotation.y +
                 (double)data.Rotation.z * data.Rotation.z + (double)data.Rotation.w * data.Rotation.w) < 0.000001 ||
                !MarbleRules.IsFinite(data.Quote.Radius) || data.Quote.Radius <= 0 ||
                !MarbleRules.IsFinite(data.Quote.Mass) || data.Quote.Mass <= 0 ||
                (data.HasRestitution && (!MarbleRules.IsFinite(data.Restitution) ||
                    data.Restitution < 0 || data.Restitution > 1))) return false;

            data.Rotation = data.Rotation.normalized;

            // 出生重叠宁可拒绝本次发射，也不让求解器把重叠球炸开；Trigger不阻挡出生。
            Physics.SyncTransforms();
            if (physicsScene.OverlapSphere(data.Position, data.Quote.Radius, overlap, ~0,
                    QueryTriggerInteraction.Ignore) > 0) return false;

            MarbleBody view = TakeFromPool();
            GameObject instance = null;
            try
            {
                instance = view != null ? view.gameObject :
                    (prefab == null ? GameObject.CreatePrimitive(PrimitiveType.Sphere) :
                    UnityEngine.Object.Instantiate(prefab));
                instance.SetActive(false);
                instance.name = "Marble " + data.Key;
                instance.transform.SetParent(null);
                if (instance.scene != scene) SceneManager.MoveGameObjectToScene(instance, scene);
                instance.transform.SetPositionAndRotation(data.Position, data.Rotation);
                instance.transform.localScale = Vector3.one * (data.Quote.Radius * 2f);

                var collider = instance.GetComponent<SphereCollider>();
                collider.enabled = true;
                collider.isTrigger = false;
                collider.center = Vector3.zero;
                collider.radius = 0.5f;

                var body = instance.GetComponent<Rigidbody>();
                if (body == null) body = instance.AddComponent<Rigidbody>();
                body.isKinematic = false;
                body.useGravity = true;
                body.detectCollisions = true;
                body.constraints = RigidbodyConstraints.None;
                body.mass = data.Quote.Mass;
                body.ResetCenterOfMass();
                body.ResetInertiaTensor();
                body.drag = 0.03f;
                body.angularDrag = 0.05f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                // 自定义模拟没有依赖自动物理插值；后续可在独立显示对象上插值。
                body.interpolation = RigidbodyInterpolation.None;
                body.solverIterations = 10;
                body.solverVelocityIterations = 4;

                if (view == null) view = instance.GetComponent<MarbleBody>();
                if (view == null) view = instance.AddComponent<MarbleBody>();
                view.Key = data.Key;
                view.Body = body;
                view.Bridge = this;
                view.Radius = data.Quote.Radius;
                view.PreviousPosition = data.Position;
                ConfigureMaterial(view, collider, data);
                bodies.Add(data.Key, view);
                instance.SetActive(true);
                body.position = data.Position;
                body.rotation = data.Rotation;
                body.velocity = data.Rotation * Vector3.forward * data.Speed;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
                return true;
            }
            catch (Exception error)
            {
                bodies.Remove(data.Key);
                if (view != null) view.Bridge = null;
                if (instance != null) { instance.SetActive(false); UnityEngine.Object.Destroy(instance); }
                Debug.LogException(error);
                return false;
            }
        }

    }
}
