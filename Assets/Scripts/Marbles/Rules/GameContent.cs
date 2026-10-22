using System;
using System.Collections.Generic;

namespace MarblesECS
{
    public enum DeviceKind { LegacyPin, Bank, Amplifier, Centrifuge, Portal, Clover, Splitter, Paddle, Lens, Capital, BloodCannon, Slow, Revive, Magnet }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OptionalBalanceFieldAttribute : Attribute { }

    [Serializable]
    public sealed class GameContent
    {
        public AttributeDefinition[] Attributes;
        public DeviceDefinition[] Devices;
        public double RushThreshold = 100;
        public double OverkillCoinsPerScore = 0.01;
        public double FlowValueExponent = 1.15;
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
            Devices = Array.ConvertAll(Devices, x => x.Copy()),
            RushThreshold = RushThreshold, OverkillCoinsPerScore = OverkillCoinsPerScore,
            FlowValueExponent = FlowValueExponent, CentimetersToUnits = CentimetersToUnits,
            MaxSplitGeneration = MaxSplitGeneration
        };
        public void Validate()
        {
            indexedAttributes = null;
            if (FlowValueExponent == 0) FlowValueExponent = 1.15;
            if (Attributes == null || Attributes.Length != (int)GameAttribute.Count) throw new ArgumentException("Incomplete attribute dictionary.");
            var ids = new HashSet<string>();
            foreach (var attribute in Attributes)
            { attribute.Validate(); if (!ids.Add(attribute.Id)) throw new ArgumentException("Repeated attribute " + attribute.Id); }
            if (RushThreshold <= 0 || !MarbleRules.IsFinite(RushThreshold) || OverkillCoinsPerScore < 0 ||
                !MarbleRules.IsFinite(OverkillCoinsPerScore) || FlowValueExponent < 1 || FlowValueExponent > 2 ||
                !MarbleRules.IsFinite(FlowValueExponent) || CentimetersToUnits <= 0 || MaxSplitGeneration < 1)
                throw new ArgumentException("Invalid content rules.");
            if (Devices == null) throw new ArgumentException("Missing content catalog.");
            foreach (var device in Devices) device.Validate();

        }
    }
}
