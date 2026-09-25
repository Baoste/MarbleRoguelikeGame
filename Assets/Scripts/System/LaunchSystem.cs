using System;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal static class LaunchSystem
    {
        internal static void Execute(SimulationContext context, IMarblePhysics physics, Vector3 position, Quaternion rotation)
        {
            if (AttributeRuntime.Enabled(context)) { ContentSpawnSystem.Launch(context, position, rotation); return; }
            var round = context.Round;
            if (round.Phase != (byte)RoundPhase.Playing) return;
            var launcher = context.Launcher;
            if ((launcher.FireHeld == 0 && launcher.PendingSingleShots == 0) || round.Time < launcher.NextFireAt) return;
            if (round.ActiveMarbleCount >= context.Tuning.MaxActiveMarbles) return;
            var quote = MarbleRules.Quote(context.Tuning, launcher.Flow);
            var player = context.Player;
            if (player.Blood < quote.Cost) return;

            var key = new MarbleKey(round.RoundId, checked(context.NextSpawnSequence + 1));
            Quaternion launchRotation = rotation.normalized;
            Vector3 launchVelocity = launchRotation * Vector3.down * context.Tuning.LaunchSpeed;
            Entity entity = Entity.Null;
            bool committed = false;
            try
            {
                entity = MarbleFactory.Create(context, key, quote);
                if (!physics.TrySpawn(new MarbleSpawnData
                {
                    Key = key, Position = position, Rotation = launchRotation, Quote = quote,
                    LinearVelocity = launchVelocity, HasRestitution = true,
                    Restitution = context.Manager.GetComponentData<MarblePhysicsProperties>(entity).Restitution
                })) return;

                context.Manager.SetComponentData(entity, new Transform3D { Position = position, Rotation = launchRotation });
                context.Manager.SetComponentData(entity, new Motion3D { LinearVelocity = launchVelocity });
                // Publish the mapping before committing resources. A failed bridge never consumes blood or cooldown.
                context.Marbles.Add(key, entity);
                player.Blood -= quote.Cost;
                launcher.NextFireAt = round.Time + context.Tuning.FireIntervalSeconds;
                if (launcher.PendingSingleShots > 0) launcher.PendingSingleShots--;
                round.ActiveMarbleCount++;
                context.Player = player;
                context.Launcher = launcher;
                context.Round = round;
                context.NextSpawnSequence = key.Sequence;
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    context.Marbles.Remove(key);
                    try { physics.Remove(key); }
                    finally
                    {
                        if (entity != Entity.Null && context.Manager.Exists(entity)) context.Manager.DestroyEntity(entity);
                    }
                }
            }
        }
    }
}
