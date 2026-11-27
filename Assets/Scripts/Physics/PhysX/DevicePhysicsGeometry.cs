using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Copies the authored collision hierarchy and shares its meshes and materials.</summary>
    internal static class DevicePhysicsGeometry
    {
        internal static void Copy(GameObject prefab, GameObject instance)
        {
            var source = prefab.transform;
            var volume = source.Find(DeviceResourceCatalog.PlacementVolumeName);
            var nodes = new Dictionary<Transform, Transform> { [source] = instance.transform };
            instance.layer = prefab.layer;
            instance.tag = prefab.tag;
            instance.transform.localScale = source.localScale;

            foreach (var body in prefab.GetComponentsInChildren<Rigidbody>(true))
                if (!body.transform.IsChildOf(volume))
                    CopyBody(body, Node(body.transform, nodes).gameObject);

            bool hasActiveCollider = false;
            foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (collider.transform.IsChildOf(volume)) continue;
                CopyCollider(collider, Node(collider.transform, nodes).gameObject);
                hasActiveCollider |= collider.enabled && IsActiveUnderRoot(collider.transform, source);
            }
            if (!hasActiveCollider) throw new InvalidOperationException(prefab.name + " has no enabled gameplay collider.");
        }

        private static bool IsActiveUnderRoot(Transform node, Transform root)
        {
            for (; node != root; node = node.parent)
                if (!node.gameObject.activeSelf) return false;
            return true;
        }

        private static Transform Node(Transform source, Dictionary<Transform, Transform> nodes)
        {
            if (nodes.TryGetValue(source, out var existing)) return existing;
            var parent = Node(source.parent, nodes);
            var node = new GameObject(source.name) { layer = source.gameObject.layer, tag = source.gameObject.tag };
            node.transform.SetParent(parent, false);
            node.transform.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            node.transform.localScale = source.localScale;
            node.SetActive(source.gameObject.activeSelf);
            nodes.Add(source, node.transform);
            return node.transform;
        }

        internal static void CopyBody(Rigidbody source, GameObject node)
        {
            var body = node.AddComponent<Rigidbody>();
            body.mass = source.mass;
            body.drag = source.drag;
            body.angularDrag = source.angularDrag;
            body.useGravity = source.useGravity;
            body.isKinematic = source.isKinematic;
            body.interpolation = source.interpolation;
            body.constraints = source.constraints;
            body.collisionDetectionMode = source.collisionDetectionMode;
            body.detectCollisions = source.detectCollisions;
            body.includeLayers = source.includeLayers;
            body.excludeLayers = source.excludeLayers;
            if (!source.automaticCenterOfMass) body.centerOfMass = source.centerOfMass;
            if (!source.automaticInertiaTensor)
            {
                body.inertiaTensorRotation = source.inertiaTensorRotation;
                body.inertiaTensor = source.inertiaTensor;
            }
            body.solverIterations = source.solverIterations;
            body.solverVelocityIterations = source.solverVelocityIterations;
        }

        private static void CopyCollider(Collider source, GameObject node)
        {
            Collider copy;
            switch (source)
            {
                case BoxCollider box:
                    var boxCopy = node.AddComponent<BoxCollider>();
                    boxCopy.center = box.center;
                    boxCopy.size = box.size;
                    copy = boxCopy;
                    break;
                case SphereCollider sphere:
                    var sphereCopy = node.AddComponent<SphereCollider>();
                    sphereCopy.center = sphere.center;
                    sphereCopy.radius = sphere.radius;
                    copy = sphereCopy;
                    break;
                case CapsuleCollider capsule:
                    var capsuleCopy = node.AddComponent<CapsuleCollider>();
                    capsuleCopy.center = capsule.center;
                    capsuleCopy.radius = capsule.radius;
                    capsuleCopy.height = capsule.height;
                    capsuleCopy.direction = capsule.direction;
                    copy = capsuleCopy;
                    break;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    var meshCopy = node.AddComponent<MeshCollider>();
                    meshCopy.cookingOptions = mesh.cookingOptions;
                    meshCopy.convex = mesh.convex;
                    meshCopy.sharedMesh = mesh.sharedMesh;
                    copy = meshCopy;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported device collider on " + source.name + ": " + source.GetType().Name);
            }
            copy.sharedMaterial = source.sharedMaterial;
            copy.contactOffset = source.contactOffset;
            copy.includeLayers = source.includeLayers;
            copy.excludeLayers = source.excludeLayers;
            copy.layerOverridePriority = source.layerOverridePriority;
            copy.isTrigger = source.isTrigger;
            copy.enabled = source.enabled;
        }
    }
}
