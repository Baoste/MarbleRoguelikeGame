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
                float restitution = context.Tuning.BaseRestitution;
                if (AttributeRuntime.Enabled(context))
                    restitution = (float)AttributeRuntime.Prepare(context, context.Launcher.Flow).Values[(int)GameAttribute.BALL_BOUNCE];
                return new MarbleSnapshot
                {
                    RoundId = round.RoundId, Tick = round.Tick, Phase = (RoundPhase)round.Phase,
                    Blood = player.Blood, Score = round.Score, TargetScore = round.TargetScore,
                    ActiveMarbles = round.ActiveMarbleCount, TimeSeconds = round.Time,
                    RushSecondsRemaining = round.RushRemaining,
                    RushMeter = round.RushMeter, RushMaximum = RushSystem.Threshold,
                    ShotsPerSecond = 1 / RushSystem.FireInterval(context),
                    Flow = context.Launcher.Flow,
                    NextMarbleRestitution = restitution
                };
            }
        }

    }
}
