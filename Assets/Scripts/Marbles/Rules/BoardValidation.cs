using System;

namespace MarblesECS
{
    internal static class BoardValidation
    {
        internal static void Validate(BoardTuning b)
        {
            BalanceGuard.Range(b.Width, 4, 50, nameof(b.Width));
            BalanceGuard.Range(b.Length, 6, 80, nameof(b.Length));
            BalanceGuard.Range(b.Thickness, 0.05, 2, nameof(b.Thickness));
            BalanceGuard.Range(b.TiltDegrees, 5, 70, nameof(b.TiltDegrees));
            BalanceGuard.Range(b.WallHeight, 0.5, 5, nameof(b.WallHeight));
            BalanceGuard.Range(b.WallThickness, 0.05, 1, nameof(b.WallThickness));
            BalanceGuard.Range(b.PlacementClearance, 0, 1, nameof(b.PlacementClearance));
            BalanceGuard.Range(b.PinHeight, 0.5, b.WallHeight, nameof(b.PinHeight));
            BalanceGuard.Range(b.GridSize, 0, 2, nameof(b.GridSize));
            BalanceGuard.Range(b.PlacementMinX, -b.Width / 2, b.Width / 2, nameof(b.PlacementMinX));
            BalanceGuard.Range(b.PlacementMaxX, b.PlacementMinX + 0.5, b.Width / 2, nameof(b.PlacementMaxX));
            BalanceGuard.Range(b.PlacementMinZ, -b.Length / 2, b.Length / 2, nameof(b.PlacementMinZ));
            BalanceGuard.Range(b.PlacementMaxZ, b.PlacementMinZ + 0.5, b.Length / 2, nameof(b.PlacementMaxZ));
            BalanceGuard.Range(b.LauncherMoveHalfWidth, 0, b.Width / 2 - 0.5, nameof(b.LauncherMoveHalfWidth));
            BalanceGuard.Range(b.LauncherX, -b.Width / 2 + 0.5, b.Width / 2 - 0.5, nameof(b.LauncherX));
            BalanceGuard.Range(b.LauncherZ, b.PlacementMaxZ + 0.3, b.Length / 2 - 0.3, nameof(b.LauncherZ));
            BalanceGuard.Range(b.LauncherHeight, 0.1, b.WallHeight, nameof(b.LauncherHeight));
            BalanceGuard.Range(b.LaunchAngleDegrees, 0, 30, nameof(b.LaunchAngleDegrees));
            BalanceGuard.Range(b.LauncherMoveSpeed, 0.1, 30, nameof(b.LauncherMoveSpeed));
            BalanceGuard.Range(b.FixedStepSeconds, 0.002, 0.03, nameof(b.FixedStepSeconds));
            BalanceGuard.Range(b.MaxStepsPerFrame, 1, 16, nameof(b.MaxStepsPerFrame));
            ValidatePins(b);
        }

        private static void ValidatePins(BoardTuning b)
        {
            BalanceGuard.Range(b.FixedPinRows, 1, 30, nameof(b.FixedPinRows));
            BalanceGuard.Range(b.FixedPinColumns, 1, 30, nameof(b.FixedPinColumns));
            BalanceGuard.Range(b.FixedPinRadius, 0.05, 0.5, nameof(b.FixedPinRadius));
            BalanceGuard.Range(b.FixedPinSpacingX, b.FixedPinRadius * 2 + 0.1, 10, nameof(b.FixedPinSpacingX));
            BalanceGuard.Range(b.FixedPinSpacingZ, b.FixedPinRadius * 2 + 0.1, 10, nameof(b.FixedPinSpacingZ));
            BalanceGuard.Range(b.FixedPinStartZ, -b.Length / 2 + b.FixedPinRadius, b.Length / 2 - b.FixedPinRadius, nameof(b.FixedPinStartZ));
            double right = (b.FixedPinColumns - 1) * 0.5 * b.FixedPinSpacingX;
            if (right + b.FixedPinRadius >= b.Width / 2 - b.WallThickness ||
                b.FixedPinStartZ - (b.FixedPinRows - 1) * b.FixedPinSpacingZ - b.FixedPinRadius <= -b.Length / 2)
                throw new ArgumentException("Fixed pin layout extends beyond the board.");
        }
    }
}
