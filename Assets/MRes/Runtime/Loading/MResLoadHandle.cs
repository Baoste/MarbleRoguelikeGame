using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MRes
{
    /// <summary>Owns all objects created by one load. Dispose on Unity's main thread.</summary>
    public sealed class MResLoadHandle : IDisposable
    {
        private readonly List<Object> owned = new List<Object>();
        private bool disposed;
        internal MResRuntimeBehaviour Behaviour;
        public GameObject Root { get; internal set; }
        public MResMetadata Metadata { get; }
        public bool IsDisposed => disposed;
        internal MResLoadHandle(MResMetadata metadata) { Metadata = metadata; }
        internal T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        public void Dispose() => Release(false);
        internal void RootDestroyed() => Release(true);
        private void Release(bool rootAlreadyDestroying)
        {
            if (disposed) return;
            disposed = true;
            Behaviour?.Release(); Behaviour = null;
            if (Root != null && !rootAlreadyDestroying) Root.SetActive(false);
            // Track every node too: even objects detached by scripts and partial builds are reclaimed.
            // Children go first, then materials/physics/textures/meshes in reverse creation order.
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var value = owned[i];
                if (value == null || (rootAlreadyDestroying && value is GameObject go &&
                    (go == Root || (Root != null && go.transform.IsChildOf(Root.transform))))) continue;
                Destroy(value);
            }
            owned.Clear(); Root = null;
        }
        internal static void Destroy(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
