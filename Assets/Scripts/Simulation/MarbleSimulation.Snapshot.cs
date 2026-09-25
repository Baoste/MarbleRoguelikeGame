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
                float restitution = BounceDrugSystem.Restitution(context, out int doseCount, out double bounceRemaining);
                if (AttributeRuntime.Enabled(context))
                    restitution = (float)AttributeRuntime.Prepare(context, context.Launcher.Flow).Values[(int)GameAttribute.BALL_BOUNCE];
                double drugRemaining = 0;
                var effects = context.Manager.GetBuffer<ActiveScoreEffectData>(context.PlayerEntity);
                for (int i = 0; i < effects.Length; i++)
                    drugRemaining = Math.Max(drugRemaining, effects[i].EndsAt - round.Time);
                return new MarbleSnapshot
                {
                    RoundId = round.RoundId, Tick = round.Tick, Phase = (RoundPhase)round.Phase,
                    Blood = player.Blood, Score = round.Score, TargetScore = round.TargetScore,
                    ActiveMarbles = round.ActiveMarbleCount, TimeSeconds = round.Time,
                    RushSecondsRemaining = AttributeRuntime.Enabled(context) ? round.RushRemaining : Math.Max(0, round.RushEndTick - round.Tick) *
                        (double)context.StepSeconds * context.Tuning.RushTimeScale,
                    DrugSecondsRemaining = Math.Max(0, drugRemaining),
                    DrugMultiplier = player.DrugScoreMultiplier, Flow = context.Launcher.Flow,
                    NextMarbleRestitution = restitution, ActiveBounceDrugs = doseCount,
                    BounceDrugSecondsRemaining = bounceRemaining
                };
            }
        }

    }
}
