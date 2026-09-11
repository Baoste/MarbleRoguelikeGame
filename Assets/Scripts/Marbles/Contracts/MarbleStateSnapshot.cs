namespace MarblesECS
{
    /// <summary>Detached gameplay state for one live marble.</summary>
    public struct MarbleStateSnapshot
    {
        public MarbleKey Key;
        public ulong StableId;
        public uint DefinitionId;
        public float BloodInvestment;
        public double BaseScore, ScoreBonus, ScoreMultiplier;
        public double BaseRushChance, RushChanceBonus;
        public float Restitution;
    }
}
