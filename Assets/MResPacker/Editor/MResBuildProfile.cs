using System;
using UnityEngine;

namespace MResPacker
{
    [Serializable]
    public sealed class MResMetadataData
    {
        public string name = "New Device";
        [TextArea] public string description = "Device description";
        [Tooltip("出售一个装置实例获得的金币，不使用购买价折算。")]
        public float value = 120;
        public float purchasePrice = 5;
        public string rarity = "common";
        [Tooltip("每回合触发次数；-1 表示无限耐久。")]
        public int durability = 20;
        public string type = "device";
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Metadata name and description are required.");
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentException("Metadata value must be finite and nonnegative.");
            if (type != "device" && type != "medicine") throw new ArgumentException("Metadata type must be device or medicine.");
            if (float.IsNaN(purchasePrice) || float.IsInfinity(purchasePrice) || purchasePrice < 1)
                throw new ArgumentException("Purchase price must be finite and at least 1.");
            if (type == "device" && (value != Math.Truncate(value) || purchasePrice != Math.Truncate(purchasePrice)))
                throw new ArgumentException("Device purchase/sale prices must be whole coins.");
            if (rarity != "common" && rarity != "rare" && rarity != "epic" && rarity != "legendary")
                throw new ArgumentException("Rarity must be common, rare, epic or legendary.");
            if (durability != -1 && (durability < 1 || durability > 10000)) throw new ArgumentException("Durability must be -1 (unlimited) or an integer in [1, 10000].");
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
