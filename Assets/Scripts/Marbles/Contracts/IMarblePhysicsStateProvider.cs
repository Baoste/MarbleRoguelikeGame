namespace MarblesECS
{
    /// <summary>Optional full-state readback for backends that expose rigidbody motion.</summary>
    public interface IMarblePhysicsStateProvider
    {
        bool TryGetState(MarbleKey key, out MarblePhysicsState state);
    }
}
