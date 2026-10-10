using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialType = UnityEngine.PhysicsMaterial;
#else
using PhysicsMaterialType = UnityEngine.PhysicMaterial;
#endif

namespace MRes
{
    public static class MResLoader
    {
        /// <summary>Explicit registry used by one-argument Load overloads; no shader fallback.</summary>
        public static MResShaderRegistry ShaderRegistry { get; set; }

        public static MResPackage Read(string filePath)
        { using (var stream = File.OpenRead(filePath)) return Read(stream); }
        public static MResPackage Read(byte[] bytes) => MResReader.Read(bytes);
        public static MResPackage Read(Stream stream) => MResReader.Read(stream);

        public static MResLoadHandle Load(string filePath) => Load(Read(filePath), ShaderRegistry);
        public static MResLoadHandle Load(byte[] bytes) => Load(Read(bytes), ShaderRegistry);
        public static MResLoadHandle Load(Stream stream) => Load(Read(stream), ShaderRegistry);
        public static MResLoadHandle Load(string filePath, MResShaderRegistry registry) => Load(Read(filePath), registry);
        public static MResLoadHandle Load(byte[] bytes, MResShaderRegistry registry) => Load(Read(bytes), registry);
        public static MResLoadHandle Load(Stream stream, MResShaderRegistry registry) => Load(Read(stream), registry);

        /// <summary>Instantiate validated data. Must be called on Unity's main thread.</summary>
        public static MResLoadHandle Load(MResPackage package, MResShaderRegistry registry, MResLoadOptions options = null)
        {
            options = options ?? new MResLoadOptions();
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (package.Materials.Count > 0 && registry == null) throw new MResShaderException("Set MResLoader.ShaderRegistry or pass a registry to Load.");
            var handle = new MResLoadHandle(package.Metadata);
            try
            {
                var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
                var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
                var physics = new Dictionary<string, PhysicsMaterialType>(StringComparer.Ordinal);
                var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
                foreach (var pair in package.Textures) textures.Add(pair.Key, MResTextureReader.Create(pair.Value, handle));
                foreach (var pair in package.Meshes) meshes.Add(pair.Key, MResMeshReader.Create(pair.Value, handle));
                foreach (var pair in package.PhysicsMaterials) physics.Add(pair.Key, MResPhysicsMaterialReader.Create(pair.Value, handle));
                foreach (var pair in package.Materials)
                    materials.Add(pair.Key, MResMaterialReader.Create(pair.Value, registry.Resolve(pair.Value.ShaderId), textures, handle));
                var nodes = MResPrefabBuilder.Build(package.Prefab, meshes, materials, physics, handle);
                handle.Root.AddComponent<MResLifetime>().Handle = handle;
                if (options.ExecuteLua && package.LuaSource != null)
                {
                    handle.Behaviour = handle.Root.AddComponent<MResRuntimeBehaviour>();
                    handle.Behaviour.Initialize(package.LuaSource, handle.Metadata, package.Name, options.SharedScript);
                    MResPhysicsRelay.Attach(nodes.Values, handle.Behaviour);
                }
                MResPrefabBuilder.Activate(package.Prefab, nodes, options.ActivateRoot);
                return handle;
            }
            catch (Exception e)
            {
                handle.Dispose();
                if (e is MResException) throw;
                throw new MResInstantiationException("Failed to instantiate MRes package " + package.Name, e);
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { ShaderRegistry = null; }
    }
}
