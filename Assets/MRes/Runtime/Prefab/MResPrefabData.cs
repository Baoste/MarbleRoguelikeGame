using System.Collections.Generic;
using UnityEngine;

namespace MRes
{
    internal sealed class MResPrefabData
    {
        internal readonly List<MResNodeData> Nodes = new List<MResNodeData>();
        internal int RootId;

        internal static MResPrefabData Parse(MResFields f, MResPackage package)
        {
            var data = new MResPrefabData(); var byId = new Dictionary<int, MResNodeData>();
            var list = f.Array("nodes");
            if (list.Count == 0 || list.Count > MResFormat.MaxNodes) throw f.Error("nodes");
            int roots = 0;
            foreach (object item in list)
            {
                var n = MResFields.From(item, f.Context + "/nodes");
                var node = new MResNodeData { Id = n.Int("id"), Parent = n.Int("parentId"), Name = n.String("name"),
                    Active = n.Bool("active", true), Layer = n.Int("layer", 0, true) };
                if (node.Id < 0 || node.Parent < -1 || node.Parent == node.Id || byId.ContainsKey(node.Id) || node.Layer < 0 || node.Layer > 31)
                    throw n.Error("id/parentId/layer");
                var t = n.Object("transform");
                node.Position = MResValues.V3(t.Object("localPosition"));
                var r = MResValues.V4(t.Object("localRotation"));
                if (Mathf.Abs(r.sqrMagnitude - 1) > 0.001f) throw t.Error("localRotation (unit quaternion required)");
                node.Rotation = new Quaternion(r.x, r.y, r.z, r.w);
                node.Scale = MResValues.V3(t.Object("localScale"));
                if (node.Parent == -1) { roots++; data.RootId = node.Id; }
                var unique = new HashSet<string>();
                foreach (object component in n.Array("components"))
                {
                    var c = MResComponentData.Parse(MResFields.From(component, n.Context + "/" + node.Id), package);
                    if ((c.Type == "Rigidbody" || c.Type == "MeshFilter" || c.Type == "MeshRenderer") && !unique.Add(c.Type))
                        throw n.Error("duplicate " + c.Type);
                    node.Components.Add(c);
                }
                byId.Add(node.Id, node); data.Nodes.Add(node);
            }
            if (roots != 1) throw f.Error("exactly one root parentId=-1 required");
            // Iterative graph walk avoids stack overflow on deep but valid hierarchies.
            var complete = new HashSet<int>();
            foreach (var node in data.Nodes)
            {
                var chain = new HashSet<int>(); var current = node;
                while (current != null && !complete.Contains(current.Id))
                {
                    if (!chain.Add(current.Id)) throw f.Error("cyclic hierarchy");
                    if (current.Parent == -1) current = null;
                    else if (!byId.TryGetValue(current.Parent, out current)) throw f.Error("missing parent");
                }
                complete.UnionWith(chain);
            }
            foreach (var node in data.Nodes)
            {
                foreach (var collider in node.Components)
                {
                    if (collider.Type != "MeshCollider") continue;
                    var mesh = package.Meshes[collider.Mesh];
                    foreach (var topology in mesh.Topologies)
                        if (topology != MeshTopology.Triangles) throw f.Error("MeshCollider requires triangle submeshes");
                    if (collider.Convex) continue;
                    var current = node;
                    while (current != null)
                    {
                        var body = current.Components.Find(c => c.Type == "Rigidbody");
                        if (body != null)
                        { if (!body.Kinematic) throw f.Error("non-convex MeshCollider on dynamic Rigidbody"); break; }
                        current = current.Parent == -1 ? null : byId[current.Parent];
                    }
                }
            }
            return data;
        }
    }

    internal sealed class MResNodeData
    {
        internal int Id, Parent, Layer;
        internal string Name;
        internal bool Active;
        internal Vector3 Position, Scale;
        internal Quaternion Rotation;
        internal readonly List<MResComponentData> Components = new List<MResComponentData>();
    }

    internal sealed class MResComponentData
    {
        internal string Type, Mesh, PhysicsMaterial;
        internal string[] Materials;
        internal bool Enabled = true, Trigger, Convex, ProvidesContacts, ModifiableContacts;
        internal int IncludeLayers, ExcludeLayers, LayerPriority, Direction;
        internal float ContactOffset = 0.01f, Radius = 0.5f, Height = 2;
        internal Vector3 Center, Size = Vector3.one;
        internal float Mass = 1, Drag, AngularDrag = 0.05f;
        internal bool Gravity = true, Kinematic;
        internal RigidbodyInterpolation Interpolation;
        internal CollisionDetectionMode Detection;
        internal RigidbodyConstraints Constraints;

        internal static MResComponentData Parse(MResFields f, MResPackage package)
        {
            var c = new MResComponentData { Type = f.String("type") };
            switch (c.Type)
            {
                case "MeshFilter":
                    c.Mesh = f.String("mesh", optional: true);
                    MResPackage.Reference(package.Meshes, c.Mesh, f.Context, true); return c;
                case "MeshRenderer":
                    c.Enabled = f.Bool("enabled", true);
                    var slots = f.Array("materials"); c.Materials = new string[slots.Count];
                    for (int i = 0; i < slots.Count; i++)
                    {
                        if (slots[i] != null && !(slots[i] is string)) throw f.Error("materials");
                        c.Materials[i] = (string)slots[i];
                        MResPackage.Reference(package.Materials, c.Materials[i], f.Context, true);
                    }
                    return c;
                case "Rigidbody":
                    c.Mass = MResValues.Nonnegative(f, "mass", 1); if (c.Mass == 0) throw f.Error("mass");
                    c.Drag = MResValues.Nonnegative(f, "drag", 0); c.AngularDrag = MResValues.Nonnegative(f, "angularDrag", 0.05f);
                    c.Gravity = f.Bool("useGravity", true); c.Kinematic = f.Bool("isKinematic");
                    c.Interpolation = MResValues.Enum<RigidbodyInterpolation>(f, "interpolation", 0);
                    c.Detection = MResValues.Enum<CollisionDetectionMode>(f, "collisionDetectionMode", 0);
                    int constraints = f.Int("constraints", 0, true);
                    if ((constraints & ~126) != 0) throw f.Error("constraints"); c.Constraints = (RigidbodyConstraints)constraints;
                    if (c.Kinematic && c.Detection != CollisionDetectionMode.Discrete && c.Detection != CollisionDetectionMode.ContinuousSpeculative)
                        throw f.Error("kinematic collisionDetectionMode");
                    return c;
                case "BoxCollider": case "SphereCollider": case "CapsuleCollider": case "MeshCollider": break;
                default: throw new MResFormatException("Unsupported prefab component (Lua belongs to manifest): " + c.Type);
            }
            c.Enabled = f.Bool("enabled", true); c.Trigger = f.Bool("isTrigger");
            c.ContactOffset = MResValues.Nonnegative(f, "contactOffset", 0.01f); if (c.ContactOffset == 0) throw f.Error("contactOffset");
            c.PhysicsMaterial = f.String("physicsMaterial", optional: true);
            MResPackage.Reference(package.PhysicsMaterials, c.PhysicsMaterial, f.Context, true);
            c.IncludeLayers = f.Int("includeLayers", 0, true); c.ExcludeLayers = f.Int("excludeLayers", 0, true);
            c.LayerPriority = f.Int("layerOverridePriority", 0, true);
            c.ProvidesContacts = f.Bool("providesContacts"); c.ModifiableContacts = f.Bool("hasModifiableContacts");
            if (c.Type != "MeshCollider" && f.Has("center")) c.Center = MResValues.V3(f.Object("center"));
            switch (c.Type)
            {
                case "BoxCollider":
                    c.Size = MResValues.V3(f.Object("size"));
                    if (c.Size.x <= 0 || c.Size.y <= 0 || c.Size.z <= 0) throw f.Error("size"); break;
                case "SphereCollider": case "CapsuleCollider":
                    c.Radius = MResValues.Nonnegative(f, "radius", 0.5f); if (c.Radius == 0) throw f.Error("radius");
                    if (c.Type == "CapsuleCollider")
                    {
                        c.Height = MResValues.Nonnegative(f, "height", 2); c.Direction = f.Int("direction", 1, true);
                        if (c.Height < 2 * c.Radius || c.Direction < 0 || c.Direction > 2) throw f.Error("height/direction");
                    }
                    break;
                case "MeshCollider":
                    c.Mesh = f.String("mesh"); MResPackage.Reference(package.Meshes, c.Mesh, f.Context);
                    c.Convex = f.Bool("convex"); if (c.Trigger && !c.Convex) throw f.Error("trigger MeshCollider must be convex"); break;
            }
            return c;
        }
    }
}
