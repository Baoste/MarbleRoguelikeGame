using System;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public GamblingWheelSnapshot GamblingWheel
        {
            get
            {
                ThrowIfDisposed();
                var round = context.Round;
                return new GamblingWheelSnapshot
                {
                    PoolValue = round.PendingScore + round.FractionalPendingScore,
                    BallCount = round.WheelBallCount,
                    SecondsRemaining = round.WheelBallCount > 0 ? Math.Max(0, round.WheelExpiresAt - round.Time) : 0,
                    WindowSeconds = round.WheelWindowSeconds,
                    DrawCount = round.WheelDrawCount,
                    LastMultiplier = round.WheelLastMultiplier,
                    LastPool = round.WheelLastPool,
                    LastScoreChange = round.WheelLastScoreChange
                };
            }
        }
    }
}
