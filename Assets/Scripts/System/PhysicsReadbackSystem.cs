using Unity.Collections;

namespace MarblesECS
{
    internal static class PhysicsReadbackSystem
    {
        internal static void Execute(SimulationContext context, IMarblePhysics physics)
        {
            var provider = physics as IMarblePhysicsStateProvider;
            if (provider == null) return;
            using (var entities = context.MarbleQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var shot = context.Manager.GetComponentData<ShotOrigin>(entity);
                    if (!provider.TryGetState(new MarbleKey(shot.RoundId, shot.SpawnSequence), out var state)) continue;
                    context.Manager.SetComponentData(entity, new Transform3D { Position = state.Position, Rotation = state.Rotation });
                    context.Manager.SetComponentData(entity, new Motion3D { LinearVelocity = state.LinearVelocity, AngularVelocity = state.AngularVelocity });
                    context.Manager.SetComponentData(entity, new PhysicsHandle { BodyHandle = state.BodyHandle });
                }
            }
        }
    }
}
