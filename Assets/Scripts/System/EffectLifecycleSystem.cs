using System;
namespace MarblesECS
{
    // These are explicitly invoked, main-thread ECS systems, not auto-created SystemBase systems.
    // Keeping the PhysX step between the two groups is more important than automatic scheduling here.
    internal static class EffectLifecycleSystem
    {
        internal static void Execute(SimulationContext context)
        {
            using var entities = context.SnapshotMarbles();
            foreach (var entity in entities.Items)
                if (!context.Manager.GetComponentData<DespawnState>(entity).PendingDespawn)
                    MarbleModifierSystem.Aggregate(context, entity);
        }
    }
}
