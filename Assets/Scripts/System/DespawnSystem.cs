using System;
using Unity.Collections;

namespace MarblesECS
{
    internal static class DespawnSystem
    {
        internal static void Execute(SimulationContext context, IMarblePhysics physics)
        {
            var round = context.Round;
            using (var entities = context.MarbleQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (!context.Manager.GetComponentData<DespawnState>(entity).PendingDespawn) continue;
                    var id = context.Manager.GetComponentData<ShotOrigin>(entity);
                    var key = new MarbleKey(id.RoundId, id.SpawnSequence);
                    physics.Remove(key);
                    context.Marbles.Remove(key);
                    context.Manager.DestroyEntity(entity); // Also destroys the per-device visit buffer.
                    round.ActiveMarbleCount--;
                }
            }
            context.Round = round;
        }
    }
}
