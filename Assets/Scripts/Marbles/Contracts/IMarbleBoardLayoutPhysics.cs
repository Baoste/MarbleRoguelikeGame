namespace MarblesECS
{
    /// <summary>Installs the Day's authored geometry at a quiescent production boundary.</summary>
    public interface IMarbleBoardLayoutPhysics
    {
        void ValidateBoardLayout(BoardLayoutDefinition layout);
        void ApplyBoardLayout(BoardLayoutDefinition layout);
    }
}
