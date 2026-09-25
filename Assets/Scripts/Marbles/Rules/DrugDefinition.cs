using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class DrugDefinition
    {
        public uint Id;
        public string Name;
        public long Price;
        public float RestitutionMultiplier = 1;
        public float DurationSeconds = 12;
        public int StartingCount;
        [OptionalBalanceField] public DrugKind Kind;
        [OptionalBalanceField] public string SourceId;
        [OptionalBalanceField] public string Description;
        [OptionalBalanceField] public bool Stackable;
        [OptionalBalanceField] public float VolumeMultiplier = 1;
        [OptionalBalanceField] public AttributeEffect[] Effects = Array.Empty<AttributeEffect>();

        public DrugDefinition Copy()
        {
            var copy = (DrugDefinition)MemberwiseClone();
            copy.Effects = Effects == null ? Array.Empty<AttributeEffect>() : (AttributeEffect[])Effects.Clone();
            return copy;
        }

        public void Validate()
        {
            BalanceGuard.Range(Id, 1, uint.MaxValue, nameof(Id));
            BalanceGuard.Text(Name, nameof(Name));
            BalanceGuard.Range(Price, 1, BalanceGuard.MaxInteger, nameof(Price));
            BalanceGuard.Range(RestitutionMultiplier, 0.1, 4, nameof(RestitutionMultiplier));
            BalanceGuard.Range(DurationSeconds, 0.1, 300, nameof(DurationSeconds));
            BalanceGuard.Range(StartingCount, 0, 1000, nameof(StartingCount));
            BalanceGuard.Range(VolumeMultiplier, 0.01, 100, nameof(VolumeMultiplier));
        }
    }
}
