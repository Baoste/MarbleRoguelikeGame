using System;

namespace MarblesECS
{
    [Serializable]
    public sealed class BoardTuning
    {
        public float Width = 10;
        public float Length = 16;
        public float Thickness = 0.35f;
        public float TiltDegrees = 22;
        public float WallHeight = 0.85f;
        public float WallThickness = 0.3f;
        public float PlacementMinX = -4;
        public float PlacementMaxX = 4;
        public float PlacementMinZ = -4.5f;
        public float PlacementMaxZ = 5;
        public float PlacementClearance = 0.15f;
        public float PinHeight = 0.7f;
        public float GridSize = 0.5f;
        public float LauncherX;
        public float LauncherZ = 6.5f;
        public float LauncherHeight = 0.35f;
        public float LaunchAngleDegrees = 10;
        public float LauncherMoveHalfWidth = 3.8f;
        public float LauncherMoveSpeed = 4;
        public float FixedStepSeconds = 1f / 60f;
        public int MaxStepsPerFrame = 4;
        public int FixedPinRows = 5;
        public int FixedPinColumns = 7;
        public float FixedPinRadius = 0.13f;
        public float FixedPinSpacingX = 1.15f;
        public float FixedPinSpacingZ = 1.65f;
        public float FixedPinStartZ = 3.7f;

        public BoardTuning Copy() { return (BoardTuning)MemberwiseClone(); }
        public void Validate() { BoardValidation.Validate(this); }
    }
}
