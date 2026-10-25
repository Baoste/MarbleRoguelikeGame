using System;
using System.Collections.Generic;
using UnityEngine;

namespace MResPacker
{
    internal static class MResPrefabSerializer
    {
        internal static object Serialize(GameObject prefab, MResBuildContext context)
        {
            var nodes = new List<object>(); Visit(prefab.transform, -1, nodes, context);
            return MResJson.Object("nodes", nodes);
        }
        private static void Visit(Transform t, int parent, List<object> nodes, MResBuildContext context)
        {
            int id = nodes.Count; var components = new List<object>(); var q = t.localRotation;
            foreach (var component in t.GetComponents<Component>())
            {
                if (component is Transform) continue;
                try { components.Add(Component(component, context)); }
                catch (Exception e) { throw new ArgumentException(MResPrefabValidator.HierarchyPath(t) + "/" + component.GetType().Name + ": " + e.Message, e); }
            }
            nodes.Add(MResJson.Object("id", id, "parentId", parent, "name", t.name, "active", t.gameObject.activeSelf,
                "layer", t.gameObject.layer, "transform", MResJson.Object("localPosition", MResBuildContext.V3(t.localPosition),
                    "localRotation", MResBuildContext.V4(new Vector4(q.x, q.y, q.z, q.w)), "localScale", MResBuildContext.V3(t.localScale)),
                "components", components));
            foreach (Transform child in t) Visit(child, id, nodes, context);
        }
        private static object Component(Component component, MResBuildContext context)
        {
            if (component is MeshFilter filter) return MResJson.Object("type", "MeshFilter", "mesh", context.Mesh(filter.sharedMesh));
            if (component is MeshRenderer renderer)
            {
                var materials = new List<string>(); foreach (var material in renderer.sharedMaterials) materials.Add(context.Material(material));
                return MResJson.Object("type", "MeshRenderer", "enabled", renderer.enabled, "materials", materials);
            }
            if (component is Rigidbody body) return Rigidbody(body);
            if (component is Collider collider) return Collider(collider, context);
            throw new ArgumentException("Unsupported component " + component.GetType().FullName);
        }
        private static object Rigidbody(Rigidbody body)
        {
#if UNITY_6000_0_OR_NEWER
            float drag = body.linearDamping, angularDrag = body.angularDamping;
#else
            float drag = body.drag, angularDrag = body.angularDrag;
#endif
            if (body.mass <= 0 || drag < 0 || angularDrag < 0) throw new ArgumentException("Invalid rigidbody mass/damping.");
            if (body.isKinematic && body.collisionDetectionMode != CollisionDetectionMode.Discrete && body.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative)
                throw new ArgumentException("Kinematic rigidbody requires Discrete or ContinuousSpeculative.");
            return MResJson.Object("type", "Rigidbody", "mass", body.mass, "drag", drag, "angularDrag", angularDrag,
                "useGravity", body.useGravity, "isKinematic", body.isKinematic, "interpolation", (int)body.interpolation,
                "collisionDetectionMode", (int)body.collisionDetectionMode, "constraints", (int)body.constraints);
        }
        private static object Collider(Collider c, MResBuildContext context)
        {
            int include = 0, exclude = 0, priority = 0; bool provides = false, modifiable = false;
#if UNITY_2022_2_OR_NEWER
            include = c.includeLayers; exclude = c.excludeLayers; priority = c.layerOverridePriority; provides = c.providesContacts;
#endif
#if UNITY_2021_2_OR_NEWER
            modifiable = c.hasModifiableContacts;
#endif
            if (c.contactOffset <= 0) throw new ArgumentException("contactOffset must be positive.");
            var result = MResJson.Object("type", c.GetType().Name, "enabled", c.enabled, "isTrigger", c.isTrigger,
                "contactOffset", c.contactOffset, "physicsMaterial", context.PhysicsMaterial(c.sharedMaterial),
                "includeLayers", include, "excludeLayers", exclude, "layerOverridePriority", priority,
                "providesContacts", provides, "hasModifiableContacts", modifiable);
            if (MResPlacementAuthoring.IsPlacement(c)) result.Add("placementOnly", true);
            if (c is BoxCollider box)
            {
                if (box.size.x <= 0 || box.size.y <= 0 || box.size.z <= 0) throw new ArgumentException("Box size must be positive.");
                result.Add("center", MResBuildContext.V3(box.center)); result.Add("size", MResBuildContext.V3(box.size));
            }
            else if (c is SphereCollider sphere)
            {
                if (sphere.radius <= 0) throw new ArgumentException("Sphere radius must be positive.");
                result.Add("center", MResBuildContext.V3(sphere.center)); result.Add("radius", sphere.radius);
            }
            else if (c is CapsuleCollider capsule)
            {
                if (capsule.radius <= 0 || capsule.height < capsule.radius * 2 || capsule.direction < 0 || capsule.direction > 2)
                    throw new ArgumentException("Invalid capsule radius/height/direction.");
                result.Add("center", MResBuildContext.V3(capsule.center)); result.Add("radius", capsule.radius);
                result.Add("height", capsule.height); result.Add("direction", capsule.direction);
            }
            else if (c is MeshCollider mesh)
            {
                if (mesh.sharedMesh == null) throw new ArgumentException("MeshCollider needs a mesh.");
                if (!mesh.convex && (mesh.isTrigger || (mesh.attachedRigidbody != null && !mesh.attachedRigidbody.isKinematic)))
                    throw new ArgumentException("Non-convex MeshCollider cannot be a trigger or belong to a dynamic body.");
                for (int i = 0; i < mesh.sharedMesh.subMeshCount; i++)
                    if (mesh.sharedMesh.GetTopology(i) != MeshTopology.Triangles) throw new ArgumentException("MeshCollider requires triangle topology.");
                result.Add("mesh", context.Mesh(mesh.sharedMesh)); result.Add("convex", mesh.convex);
            }
            else throw new ArgumentException("Unsupported collider " + c.GetType().Name);
            return result;
        }
    }
}
