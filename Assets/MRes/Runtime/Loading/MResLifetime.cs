using UnityEngine;

namespace MRes
{
    [AddComponentMenu("")]
    internal sealed class MResLifetime : MonoBehaviour
    {
        internal MResLoadHandle Handle;
        private void OnDestroy() { Handle?.RootDestroyed(); Handle = null; }
    }
}
