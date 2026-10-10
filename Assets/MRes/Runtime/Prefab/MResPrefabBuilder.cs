using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialType = UnityEngine.PhysicsMaterial;
#else
using PhysicsMaterialType = UnityEngine.PhysicMaterial;
#endif

namespace MRes
{
    internal static class MResPrefabBuilder
    {
        internal static Dictionary<int, GameObject> Build(MResPrefabData data, Dictionary<string, Mesh> meshes,
            Dictionary<string, Material> materials, Dictionary<string, PhysicsMaterialType> physics, MResLoadHandle handle)
        {
            var nodes = new Dictionary<int, GameObject>();
            foreach (var n in data.Nodes)
            {
                var go = handle.Own(new GameObject(n.Name)); go.SetActive(false); go.layer = n.Layer; nodes.Add(n.Id, go);
            }
            handle.Root = nodes[data.RootId];
            foreach (var n in data.Nodes)
            {
                var t = nodes[n.Id].transform;
                if (n.Parent != -1) t.SetParent(nodes[n.Parent].transform, false);
                t.localPosition = n.Position; t.localRotation = n.Rotation; t.localScale = n.Scale;
            }
            foreach (var n in data.Nodes)
            {
                var go = nodes[n.Id];
                foreach (var c in n.Components)
                {
                    if (c.Type == "MeshFilter") go.AddComponent<MeshFilter>().sharedMesh = c.Mesh == null ? null : meshes[c.Mesh];
                    if (c.Type == "MeshRenderer")
                    {
                        var renderer = go.AddComponent<MeshRenderer>(); renderer.enabled = c.Enabled;
                        var slots = new Material[c.Materials.Length];
                        for (int i = 0; i < slots.Length; i++) slots[i] = c.Materials[i] == null ? null : materials[c.Materials[i]];
                        renderer.sharedMaterials = slots;
                    }
                }
            }
            foreach (var n in data.Nodes)
                foreach (var c in n.Components)
                    if (c.Type == "Rigidbody") AddBody(nodes[n.Id], c);
            foreach (var n in data.Nodes)
                foreach (var c in n.Components)
                    if (c.Type.EndsWith("Collider", System.StringComparison.Ordinal)) AddCollider(nodes[n.Id], c, meshes, physics);
            return nodes;
        }
        private static void AddBody(GameObject go, MResComponentData c)
        {
            var body = go.AddComponent<Rigidbody>(); body.mass = c.Mass;
#if UNITY_6000_0_OR_NEWER
            body.linearDamping = c.Drag; body.angularDamping = c.AngularDrag;
#else
            body.drag = c.Drag; body.angularDrag = c.AngularDrag;
#endif
            body.useGravity = c.Gravity; body.isKinematic = c.Kinematic; body.interpolation = c.Interpolation;
            body.collisionDetectionMode = c.Detection; body.constraints = c.Constraints;
        }
        private static void AddCollider(GameObject go, MResComponentData c, Dictionary<string, Mesh> meshes,
            Dictionary<string, PhysicsMaterialType> physics)
        {
            Collider collider;
            switch (c.Type)
            {
                case "BoxCollider":
                    var box = go.AddComponent<BoxCollider>(); box.center = c.Center; box.size = c.Size; collider = box; break;
                case "SphereCollider":
                    var sphere = go.AddComponent<SphereCollider>(); sphere.center = c.Center; sphere.radius = c.Radius; collider = sphere; break;
                case "CapsuleCollider":
                    var capsule = go.AddComponent<CapsuleCollider>(); capsule.center = c.Center; capsule.radius = c.Radius;
                    capsule.height = c.Height; capsule.direction = c.Direction; collider = capsule; break;
                default:
                    var mesh = go.AddComponent<MeshCollider>(); mesh.convex = c.Convex; mesh.sharedMesh = meshes[c.Mesh]; collider = mesh; break;
            }
            collider.enabled = c.Enabled; collider.isTrigger = c.Trigger; collider.contactOffset = c.ContactOffset;
            collider.sharedMaterial = c.PhysicsMaterial == null ? null : physics[c.PhysicsMaterial];
#if UNITY_2022_2_OR_NEWER
            collider.includeLayers = c.IncludeLayers; collider.excludeLayers = c.ExcludeLayers;
            collider.layerOverridePriority = c.LayerPriority; collider.providesContacts = c.ProvidesContacts;
#endif
#if UNITY_2021_2_OR_NEWER
            collider.hasModifiableContacts = c.ModifiableContacts;
#endif
            var activation = go.GetComponent<MResColliderActivation>();
            if (activation == null) activation = go.AddComponent<MResColliderActivation>();
            activation.Track(collider, c);
        }
        internal static void Activate(MResPrefabData data, Dictionary<int, GameObject> nodes, bool activateRoot = true)
        {
            foreach (var n in data.Nodes) if (n.Id != data.RootId) nodes[n.Id].SetActive(n.Active);
            nodes[data.RootId].SetActive(activateRoot && data.Nodes.Find(n => n.Id == data.RootId).Active);
            foreach (var node in nodes.Values) node.GetComponent<MResColliderActivation>()?.Apply();
        }
    }
}
