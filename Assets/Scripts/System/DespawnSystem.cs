using System;

namespace MarblesECS
{
    internal static class DespawnSystem
    {
        internal static void Execute(SimulationContext context, IMarblePhysics physics)
        {
            var round = context.Round;
            using (var entities = context.SnapshotMarbles())
            {
                foreach (var entity in entities.Items)
                {
                    if (!context.Manager.GetComponentData<DespawnState>(entity).PendingDespawn) continue;
                    var id = context.Manager.GetComponentData<ShotOrigin>(entity);
                    var key = new MarbleKey(id.RoundId, id.SpawnSequence);
                    BallInteractionSystem.Refund(context, entity);
                    physics.Remove(key);
                    context.UnregisterMarble(key, entity);
                    context.Manager.DestroyEntity(entity); // Also destroys the per-device visit buffer.
                    round.ActiveMarbleCount--;
                }
            }
            var updated = context.Round; updated.ActiveMarbleCount = round.ActiveMarbleCount; context.Round = updated;
        }
    }
}
