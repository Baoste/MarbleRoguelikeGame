using Unity.Entities;

namespace MarblesECS
{
    /// <summary>Independent timed doses; gameplay time pauses with the simulation.</summary>
    internal struct ActiveBounceDrugData : IBufferElementData
    {
        public double Multiplier;
        public double EndsAt;
    }
}
