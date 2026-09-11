using System;
using Unity.Collections;

namespace MarblesECS
{
    internal static class LostMarbleSystem
    {
        internal static void Execute(SimulationContext context, IMarblePhysics physics)
        {
            var round = context.Round;
            bool drainTimedOut = round.Phase == (byte)RoundPhase.Draining &&
                round.Time - round.DrainStartedAt >= context.Tuning.DrainTimeoutSeconds;
            using (var entities = context.MarbleQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var runtime = context.Manager.GetComponentData<DespawnState>(entity);
                    if (runtime.PendingDespawn) continue;
                    var id = context.Manager.GetComponentData<ShotOrigin>(entity);
                    bool exists = physics.TryGetPosition(new MarbleKey(id.RoundId, id.SpawnSequence), out var position);
                    float extent = context.Tuning.WorldBoundsExtent;
                    long expiry = context.Manager.GetComponentData<Lifetime>(entity).ExpireTick;
                    bool expired = expiry != 0 && round.Tick >= expiry;
                    bool lost = drainTimedOut || expired || !exists ||
                        !SimulationContext.IsFinite(position) || position.y < context.Tuning.DespawnBelowY ||
                        Math.Abs(position.x) > extent || Math.Abs(position.y) > extent || Math.Abs(position.z) > extent;
                    if (!lost) continue;
                    context.Manager.SetComponentData(entity, new SettlementState { IsSettled = true });
                    runtime.PendingDespawn = true;
                    runtime.Reason = drainTimedOut ? MarbleDespawnReason.RoundEnd : !exists ? MarbleDespawnReason.MissingBody :
                        expired
                        ? MarbleDespawnReason.Expired : MarbleDespawnReason.Drained;
                    context.Manager.SetComponentData(entity, runtime);
                }
            }
        }
    }
}
