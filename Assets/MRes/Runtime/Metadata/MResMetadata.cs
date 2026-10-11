using System;
using UnityEngine.Scripting;

namespace MRes
{
    public enum MResItemType { Medicine, Device }

    [Serializable, Preserve]
    public sealed class MResMetadata
    {
        public string name;
        public string description;
        public float value;
        public float purchasePrice = 5;
        public string rarity = "common";
        public int durability = 20;
        public MResItemType type;

        internal MResMetadata Copy() => (MResMetadata)MemberwiseClone();
    }
}
