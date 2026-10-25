using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    /// <summary>Separate, disabled placement sphere. Existing authored dimensions are never overwritten by Build.</summary>
    public static class MResPlacementAuthoring
    {
        public const string NodeName = "PlacementVolume";

        [MenuItem("Tools/MRes/Add Placement Sphere to Selected Profile")]
        public static void AddSelected()
        {
            var profile = Selection.activeObject as MResBuildProfile;
            if (!profile) throw new InvalidOperationException("Select an MRes build profile.");
            Ensure(profile);
            Selection.activeObject = profile.SourcePrefab;
            EditorGUIUtility.PingObject(profile.SourcePrefab);
        }

        public static void Ensure(MResBuildProfile profile)
        {
            if (!profile || profile.Metadata == null || profile.Metadata.type != "device" || !profile.SourcePrefab) return;
            if (profile.SourcePrefab.transform.Find(NodeName)) { Validate(profile.SourcePrefab); return; }
            string path = AssetDatabase.GetAssetPath(profile.SourcePrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                AddToRoot(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            profile.SourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            EditorUtility.SetDirty(profile);
        }

        public static void AddToRoot(GameObject root)
        {
            if (root.transform.Find(NodeName)) { Validate(root); return; }
            var points = new List<Vector3>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!filter.sharedMesh || !renderer || !renderer.enabled || !IsVisible(filter.transform, root.transform)) continue;
                var matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var point in filter.sharedMesh.vertices) points.Add(matrix.MultiplyPoint3x4(point));
            }
            if (points.Count == 0)
                throw new InvalidOperationException("Cannot fit a placement sphere without model vertices: " + root.name);
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            Vector3 center = bounds.center;
            float radius = Mathf.Max(.01f, points.Max(p => Vector3.Distance(center, p)));
            var child = new GameObject(NodeName); child.transform.SetParent(root.transform, false);
            child.layer = 2; // Ignore Raycast; the loader also keeps this collider disabled.
            var sphere = child.AddComponent<SphereCollider>();
            sphere.center = center; sphere.radius = radius; sphere.isTrigger = true; sphere.enabled = false;
            Validate(root);
        }

        internal static bool IsPlacement(Collider collider) => collider.transform.parent && collider.name == NodeName &&
            collider.transform.parent.parent == null;

        private static bool IsVisible(Transform node, Transform root)
        {
            // A prefab root may be authored inactive; only hidden descendants are excluded.
            for (; node != root; node = node.parent) if (!node.gameObject.activeSelf) return false;
            return true;
        }

        public static void Validate(GameObject root)
        {
            var nodes = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == NodeName).ToArray();
            if (nodes.Length != 1 || nodes[0].parent != root.transform)
                throw new InvalidOperationException("Device requires one direct child named " + NodeName + ". Build MRes can create it automatically.");
            var node = nodes[0]; var sphere = node.GetComponent<SphereCollider>();
            if (!sphere || node.GetComponents<Collider>().Length != 1 || node.childCount != 0 ||
                node.GetComponent<Renderer>() || node.GetComponent<Rigidbody>())
                throw new InvalidOperationException("PlacementVolume must contain only Transform and one SphereCollider.");
            if (!Finite(sphere.radius) || sphere.radius <= 0 || !Finite(sphere.center.x) || !Finite(sphere.center.y) || !Finite(sphere.center.z))
                throw new InvalidOperationException("Placement sphere radius and center must be finite, with positive radius.");
            var scale = node.lossyScale;
            if (Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) <= 0)
                throw new InvalidOperationException("Placement sphere cannot have zero scale.");
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
