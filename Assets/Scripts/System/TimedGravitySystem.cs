using Unity.Collections;
using Unity.Entities;

namespace MarblesECS
{
    // A temporary multiplier, separate from the ball's base attributes and other modifiers.
    internal struct TimedGravity : IComponentData
    {
        internal float Scale;
        internal double EndsAt;
        internal int SourceId;
        internal bool ResetOnOtherDevice;
    }

    internal static class TimedGravitySystem
    {
        internal static float Scale(SimulationContext c, Entity ball) =>
            c.Manager.HasComponent<TimedGravity>(ball) ? c.Manager.GetComponentData<TimedGravity>(ball).Scale : 1;

        internal static bool Set(SimulationContext c, Entity ball, int sourceId, float scale, float seconds, bool resetOnOtherDevice)
        {
            if (!BallInteractionSystem.Finite(scale) || scale < -10 || scale > 10 ||
                !BallInteractionSystem.Finite(seconds) || seconds <= 0 || seconds > 60 ||
                !(c.Physics is IMarblePhysicsCommands) || !Placed(c, sourceId) ||
                c.Manager.GetComponentData<DespawnState>(ball).PendingDespawn ||
                c.Manager.GetComponentData<SettlementState>(ball).IsSettled) return false;
            var shot = c.Manager.GetComponentData<ShotOrigin>(ball);
            if (!c.Physics.TryGetPosition(new MarbleKey(shot.RoundId, shot.SpawnSequence), out _)) return false;
            var effect = new TimedGravity { Scale = scale, EndsAt = c.Round.Time + seconds,
                SourceId = sourceId, ResetOnOtherDevice = resetOnOtherDevice };
            if (c.Manager.HasComponent<TimedGravity>(ball)) c.Manager.SetComponentData(ball, effect);
            else c.Manager.AddComponentData(ball, effect);
            Apply(c, ball);
            return true;
        }

        internal static bool Clear(SimulationContext c, Entity ball)
        {
            if (!c.Manager.HasComponent<TimedGravity>(ball)) return false;
            c.Manager.RemoveComponent<TimedGravity>(ball);
            Apply(c, ball);
            return true;
        }

        // Called before the destination's Lua, regardless of its cooldown/visit limit.
        internal static void Contact(SimulationContext c, Entity ball, int instanceId)
        {
            if (!c.Manager.HasComponent<TimedGravity>(ball)) return;
            var effect = c.Manager.GetComponentData<TimedGravity>(ball);
            if (effect.ResetOnOtherDevice && effect.SourceId != instanceId) Clear(c, ball);
        }

        // Expire after physics: the last step still receives its full interval of gravity.
        // Source removal is also checked before physics; exhaustion alone does not cancel an accepted effect.
        internal static void Tick(SimulationContext c, bool afterPhysics)
        {
            if (!AttributeRuntime.Enabled(c)) return;
            using (var balls = c.MarbleQuery.ToEntityArray(Allocator.Temp))
                foreach (var ball in balls)
                {
                    if (!c.Manager.HasComponent<TimedGravity>(ball)) continue;
                    var effect = c.Manager.GetComponentData<TimedGravity>(ball);
                    if (!Placed(c, effect.SourceId) || (afterPhysics && c.Round.Time + 1e-7 >= effect.EndsAt)) Clear(c, ball);
                }
        }

        private static bool Placed(SimulationContext c, int id) =>
            CampaignStateUtility.FindDevice(c, id, out var device) &&
            c.Manager.GetComponentData<OwnedDeviceData>(device).Placed &&
            c.Physics is IMarblePhysicsCommands commands && commands.TryGetDevicePose(id, out _);

        private static void Apply(SimulationContext c, Entity ball)
        {
            if (!(c.Physics is IMarblePhysicsCommands commands)) return;
            var shot = c.Manager.GetComponentData<ShotOrigin>(ball);
            commands.SetProperties(new MarbleKey(shot.RoundId, shot.SpawnSequence), AttributeRuntime.Properties(c, ball));
        }
    }
}
