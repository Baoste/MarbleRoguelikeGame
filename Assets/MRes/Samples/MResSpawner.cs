using UnityEngine;

namespace MRes.Samples
{
    public sealed class MResSpawner : MonoBehaviour
    {
        [Tooltip("Absolute filesystem path; overrides packageBytes when provided.")]
        public string path;
        [Tooltip("An opaque .mres.bytes TextAsset; its entries are only read in memory.")]
        public TextAsset packageBytes;
        public MResShaderRegistry shaderRegistry;
        private MResLoadHandle handle;
        public MResLoadHandle Handle => handle;

        private void Start()
        {
            handle = string.IsNullOrEmpty(path)
                ? MResLoader.Load(packageBytes.bytes, shaderRegistry)
                : MResLoader.Load(path, shaderRegistry);
            handle.Root.transform.SetParent(transform, false);
        }
        private void OnDestroy() { handle?.Dispose(); handle = null; }
    }
}
