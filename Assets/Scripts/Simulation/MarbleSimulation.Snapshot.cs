using System;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        /// <summary>Value copy; changing this snapshot never changes ECS state.</summary>
        public MarbleSnapshot Snapshot
        {
            get
            {
                ThrowIfDisposed();
                var round = context.Round;
                var player = context.Player;
                var flow = context.Launcher.Flow;
                var next = AttributeRuntime.Enabled(context) ? AttributeRuntime.Prepare(context, flow) : null;
                return new MarbleSnapshot
                {
                    RoundId = round.RoundId, Tick = round.Tick, Phase = (RoundPhase)round.Phase,
                    Blood = player.Blood, BloodCapacity = context.BloodCapacity, BloodSpent = player.BloodSpent,
                    NextMarbleBaseValue = next?.Quote.BaseValue ?? context.Tuning.BaseScore,
                    BloodValueStage = next?.BloodValueStage ?? -1,
                    Score = round.Score, TargetScore = round.TargetScore,
                    ActiveMarbles = round.ActiveMarbleCount, TimeSeconds = round.Time,
                    RushSecondsRemaining = round.RushRemaining,
                    RushDurationSeconds = round.RushDuration,
                    RushCooldownSecondsRemaining = round.RushCooldownRemaining,
                    RushMeter = round.RushMeter, RushMaximum = RushSystem.Threshold,
                    ScoreStackBonus = ScoreStackSystem.Bonus(context),
                    ScoreStackSecondsRemaining = Math.Max(0, round.ScoreStackExpiresAt - round.Time),
                    ScoreStackWindowSeconds = round.ScoreStackWindowSeconds,
                    ShotsPerSecond = 1 / RushSystem.FireInterval(context, context.Tuning.FireIntervalSeconds),
                    Flow = flow,
                    NextMarbleRestitution = next != null
                        ? (float)next.Values[(int)GameAttribute.BALL_BOUNCE] : context.Tuning.BaseRestitution
                };
            }
        }

    }
}
