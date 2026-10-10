using System;
using UnityEngine;

namespace MRes
{
    [CreateAssetMenu(menuName = "MRes/Shader Registry")]
    public sealed class MResShaderRegistry : ScriptableObject
    {
        [Serializable]
        public struct Entry { public string id; public Shader shader; }
        public Entry[] entries = Array.Empty<Entry>();

        public Shader Resolve(string shaderId)
        {
            Shader result = null;
            bool found = false;
            if (entries != null)
                foreach (var e in entries)
                {
                    if (!string.Equals(e.id, shaderId, StringComparison.Ordinal)) continue;
                    if (found) throw new MResShaderException("Duplicate shader ID: " + shaderId);
                    found = true; result = e.shader;
                }
            if (result == null) throw new MResShaderException("Unregistered shader ID: " + shaderId);
            if (!result.isSupported) throw new MResShaderException("Shader is unsupported on this platform: " + shaderId);
            return result;
        }
    }
}
