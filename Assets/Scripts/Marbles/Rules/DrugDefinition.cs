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

        public DrugDefinition Copy() { return (DrugDefinition)MemberwiseClone(); }

        public void Validate()
        {
            BalanceGuard.Range(Id, 1, uint.MaxValue, nameof(Id));
            BalanceGuard.Text(Name, nameof(Name));
            BalanceGuard.Range(Price, 1, BalanceGuard.MaxInteger, nameof(Price));
            BalanceGuard.Range(RestitutionMultiplier, 0.1, 4, nameof(RestitutionMultiplier));
            BalanceGuard.Range(DurationSeconds, 0.1, 300, nameof(DurationSeconds));
            BalanceGuard.Range(StartingCount, 0, 1000, nameof(StartingCount));
        }
    }
}
