namespace MarblesECS
{
    /// <summary>Support for a quiescent day boundary. Implementations own their scene and caches.</summary>
    public interface IMarbleDayPhysics : IMarblePhysics
    {
        void ResetDayPhysics();
        IMarbleDayPhysicsCheckpoint CaptureDayCheckpoint();
    }

    public interface IMarbleDayPhysicsCheckpoint
    {
        void Restore();
    }
}
