using System;
using Unity.Collections;

namespace MarblesECS
{
    // These are explicitly invoked, main-thread ECS systems, not auto-created SystemBase systems.
    // Keeping the PhysX step between the two groups is more important than automatic scheduling here.
    internal static class EffectLifecycleSystem
    {
        internal static void Execute(SimulationContext context)
        {
            using (var entities = context.MarbleQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                    if (!context.Manager.GetComponentData<DespawnState>(entities[i]).PendingDespawn)
                        MarbleModifierSystem.Aggregate(context, entities[i]);
        }
    }
}
