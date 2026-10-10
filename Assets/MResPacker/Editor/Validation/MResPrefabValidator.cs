using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    internal static class MResPrefabValidator
    {
        private static readonly HashSet<Type> Allowed = new HashSet<Type> { typeof(Transform), typeof(MeshFilter), typeof(MeshRenderer),
            typeof(BoxCollider), typeof(SphereCollider), typeof(CapsuleCollider), typeof(MeshCollider), typeof(Rigidbody) };
        internal static void Validate(GameObject prefab, MResAnalysisReport report)
        {
            if (prefab == null || !EditorUtility.IsPersistent(prefab) || !AssetDatabase.GetAssetPath(prefab).EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Source must be a regular .prefab asset, not a scene object or model file.");
            if (prefab.transform.parent != null) throw new ArgumentException("Select the prefab root.");
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                report.NodeCount++;
                string path = HierarchyPath(t);
                if (PrefabUtility.GetPrefabAssetType(t.gameObject) == PrefabAssetType.Variant) report.Errors.Add("Prefab Variant is unsupported: " + path);
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) { report.UnsupportedComponents.Add(path + ": Missing script"); continue; }
                    if (!Allowed.Contains(c.GetType())) report.UnsupportedComponents.Add(path + ": " + c.GetType().FullName);
                    if (c is Collider) report.ColliderCount++;
                    if (c is Rigidbody) report.RigidbodyCount++;
                }
            }
            if (report.NodeCount > 10000) report.Errors.Add("More than 10000 nodes.");
        }
        internal static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;
    }
}
