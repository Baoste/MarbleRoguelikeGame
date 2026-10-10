using System;
using System.Collections.Generic;
using UnityEngine;

namespace MResPacker
{
    [CreateAssetMenu(menuName = "MRes Packer/Shader Registry")]
    public sealed class MResPackerShaderRegistry : ScriptableObject
    {
        [Serializable] public sealed class Entry { public Shader Shader; public string ShaderId; }
        public Entry[] Entries = Array.Empty<Entry>();
        public Dictionary<Shader, string> ValidateAndMap()
        {
            var result = new Dictionary<Shader, string>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (entry == null || entry.Shader == null || string.IsNullOrWhiteSpace(entry.ShaderId))
                    throw new ArgumentException("Shader registry has an empty shader or shader ID.");
                if (result.ContainsKey(entry.Shader) || !ids.Add(entry.ShaderId))
                    throw new ArgumentException("Shader registry has a duplicate shader or shader ID: " + entry.ShaderId);
                result.Add(entry.Shader, entry.ShaderId);
            }
            return result;
        }
    }
}
