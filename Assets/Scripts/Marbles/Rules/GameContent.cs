using System;
using System.Collections.Generic;

namespace MarblesECS
{
    public enum DeviceKind { LegacyPin, Bank, Amplifier, Centrifuge, Portal, Clover, Splitter, Paddle, Lens, Capital, BloodCannon, Slow, Revive, Magnet }
    public enum DrugKind { LegacyBounce, Adrenaline, RushFruit, RushDuration, Lubricant, Coagulant, Ecstasy, ThinBlood, Replicator, Magnetic, Direction }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OptionalBalanceFieldAttribute : Attribute { }

    [Serializable]
    public sealed class GameContent
    {
        public AttributeDefinition[] Attributes;
        public DeviceDefinition[] Devices;
        public DrugDefinition[] Drugs;
        public double RushThreshold = 10;
        public double OverkillCoinsPerScore = 0.01;
        public float CentimetersToUnits = 0.01f;
        public int MaxSplitGeneration = 2;
        [NonSerialized] private AttributeDefinition[] indexedAttributes;
        public AttributeDefinition Definition(GameAttribute attribute)
        {
            if (indexedAttributes == null)
            {
                indexedAttributes = new AttributeDefinition[(int)GameAttribute.Count];
                foreach (var definition in Attributes)
                    indexedAttributes[(int)(GameAttribute)Enum.Parse(typeof(GameAttribute), definition.Id)] = definition;
            }
            return indexedAttributes[(int)attribute] ?? throw new ArgumentException("Missing attribute: " + attribute);
        }
        public GameContent Copy() => new GameContent
        {
            Attributes = Array.ConvertAll(Attributes, x => x.Copy()),
            Devices = Array.ConvertAll(Devices, x => x.Copy()), Drugs = Array.ConvertAll(Drugs, x => x.Copy()),
            RushThreshold = RushThreshold, OverkillCoinsPerScore = OverkillCoinsPerScore,
            CentimetersToUnits = CentimetersToUnits, MaxSplitGeneration = MaxSplitGeneration
        };
        public void Validate()
        {
            indexedAttributes = null;
            if (Attributes == null || Attributes.Length != (int)GameAttribute.Count) throw new ArgumentException("Incomplete attribute dictionary.");
            var ids = new HashSet<string>();
            foreach (var attribute in Attributes)
            { attribute.Validate(); if (!ids.Add(attribute.Id)) throw new ArgumentException("Repeated attribute " + attribute.Id); }
            if (RushThreshold <= 0 || !MarbleRules.IsFinite(RushThreshold) || OverkillCoinsPerScore < 0 ||
                !MarbleRules.IsFinite(OverkillCoinsPerScore) || CentimetersToUnits <= 0 || MaxSplitGeneration < 1)
                throw new ArgumentException("Invalid content rules.");
            if (Devices == null || Drugs == null) throw new ArgumentException("Missing content catalog.");
            foreach (var device in Devices) device.Validate();
            foreach (var drug in Drugs)
            {
                drug.Validate();
                foreach (var effect in drug.Effects)
                    if (!Definition(effect.Attribute).Allows(effect.Operation) || !MarbleRules.IsFinite(effect.Value))
                        throw new ArgumentException("Invalid drug effect " + drug.Name);
            }
        }
    }
}
