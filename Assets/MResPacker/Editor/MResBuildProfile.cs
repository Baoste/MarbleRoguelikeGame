using System;
using UnityEngine;

namespace MResPacker
{
    [Serializable]
    public sealed class MResMetadataData
    {
        public string name = "New Device";
        [TextArea] public string description = "Device description";
        public float value = 120;
        public string type = "device";
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Metadata name and description are required.");
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentException("Metadata value must be finite and nonnegative.");
            if (type != "device" && type != "medicine") throw new ArgumentException("Metadata type must be device or medicine.");
        }
    }
    [Serializable]
    public sealed class MResExtraFile
    {
        public string VirtualPath = "game/device.json";
        public TextAsset Source;
    }
    [CreateAssetMenu(menuName = "MRes Packer/Build Profile")]
    public sealed class MResBuildProfile : ScriptableObject
    {
        public GameObject SourcePrefab;
        [Tooltip("Select a UTF-8 .lua asset. No Lua component is needed on the prefab.")]
        public UnityEngine.Object LuaScript;
        public MResMetadataData Metadata = new MResMetadataData();
        public MResPackerShaderRegistry ShaderRegistry;
        public string OutputPath = "Assets/MResPacker/Output/BouncePad.mres";
        [Tooltip("Optional opaque business data, stored as Other entries, outside the fixed metadata schema.")]
        public MResExtraFile[] ExtraFiles = Array.Empty<MResExtraFile>();
    }
}
