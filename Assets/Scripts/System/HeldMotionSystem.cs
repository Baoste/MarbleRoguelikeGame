using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    // Only suspension state; subsequent effects are composed by Lua in OnHoldEnd.
    internal struct HeldMotion : IComponentData
    {
        internal double StartedAt, ReleaseAt;
        internal int SourceId;
        internal MarbleContact Contact;
        internal Vector3 OriginalVelocity, OriginalSpin;
        internal float Duration;
    }

    internal static class HeldMotionSystem
    {
        internal static bool Begin(SimulationContext c, Entity ball, int sourceId, MarbleContact contact, float seconds)
        {
            if (!(c.Physics is IMarblePhysicsSuspension suspension) || !(c.Physics is IMarblePhysicsCommands) ||
                !BallInteractionSystem.Finite(seconds) || seconds <= 0 || seconds > 60 ||
                !Placed(c, sourceId) ||
                c.Manager.HasComponent<HeldMotion>(ball) ||
                c.Manager.GetComponentData<DespawnState>(ball).PendingDespawn ||
                c.Manager.GetComponentData<SettlementState>(ball).IsSettled) return false;
            var shot = c.Manager.GetComponentData<ShotOrigin>(ball);
            var motion = c.Manager.GetComponentData<Motion3D>(ball);
            if (!suspension.TrySetSuspended(new MarbleKey(shot.RoundId, shot.SpawnSequence), true)) return false;
            c.Manager.AddComponentData(ball, new HeldMotion
            {
                StartedAt = c.Round.Time, ReleaseAt = c.Round.Time + seconds, Duration = seconds,
                SourceId = sourceId, Contact = contact,
                OriginalVelocity = motion.LinearVelocity, OriginalSpin = motion.AngularVelocity
            });
            motion.LinearVelocity = motion.AngularVelocity = Vector3.zero;
            c.Manager.SetComponentData(ball, motion);
            MarkMoved(c, ball);
            return true;
        }

        // Runs after readback so one second of holding includes a full second of frozen physics.
        internal static void Execute(SimulationContext c)
        {
            using (var balls = c.MarbleQuery.ToEntityArray(Allocator.Temp))
                foreach (var ball in balls)
                {
                    if (!c.Manager.HasComponent<HeldMotion>(ball)) continue;
                    var hold = c.Manager.GetComponentData<HeldMotion>(ball);
                    bool cancelled = !Placed(c, hold.SourceId);
                    if (!cancelled && c.Round.Time + 1e-7 < hold.ReleaseAt) continue;
                    var shot = c.Manager.GetComponentData<ShotOrigin>(ball);
                    c.Manager.RemoveComponent<HeldMotion>(ball);
                    if (!(c.Physics is IMarblePhysicsSuspension suspension) ||
                        !suspension.TrySetSuspended(new MarbleKey(shot.RoundId, shot.SpawnSequence), false)) continue;
                    var motion = c.Manager.GetComponentData<Motion3D>(ball);
                    motion.AngularVelocity = hold.OriginalSpin;
                    c.Manager.SetComponentData(ball, motion);
                    // Always restore first: a missing or failing Lua handler cannot strand this ball.
                    BallInteractionSystem.SetVelocity(c, ball, hold.OriginalVelocity);
                    MarkMoved(c, ball);
                    long expiry = c.Manager.GetComponentData<Lifetime>(ball).ExpireTick;
                    bool ending = (expiry != 0 && c.Round.Tick >= expiry) ||
                        (c.Round.Phase == (byte)RoundPhase.Draining && c.Round.Time - c.Round.DrainStartedAt >= c.Tuning.DrainTimeoutSeconds);
                    if (cancelled || ending || !c.TryGetFlying(new MarbleKey(shot.RoundId, shot.SpawnSequence), out _)) continue;
                    try { DeviceScriptSystem.HoldEnd(c, ball, hold); }
                    catch (MRes.MResLuaException error)
                    {
                        // Other balls due this step must still resume even if one package throws.
                        Debug.LogException(error);
                    }
                }
        }

        private static bool Placed(SimulationContext c, int id) =>
            CampaignStateUtility.FindDevice(c, id, out var device) &&
            c.Manager.GetComponentData<OwnedDeviceData>(device).Placed &&
            c.Physics is IMarblePhysicsCommands commands && commands.TryGetDevicePose(id, out _);

        private static void MarkMoved(SimulationContext c, Entity ball)
        {
            var features = c.Manager.GetComponentData<BallFeatures>(ball);
            features.MovedTick = c.Round.Tick;
            c.Manager.SetComponentData(ball, features);
        }
    }
}
