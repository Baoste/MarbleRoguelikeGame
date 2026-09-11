using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        public Vector3 LauncherLocalPosition => IsReady ? simulation.LauncherLocalPosition : Vector3.zero;

        private void StepLauncher(float deltaTime)
        {
            simulation.SetLauncherMovementInput(HorizontalInput(), AutoMoveLauncher);
            simulation.StepLauncherMovement(deltaTime);
            SyncLauncherTransform();
        }

        private void SyncLauncherTransform()
        {
            if (IsReady && LaunchPoint != null)
                LaunchPoint.localPosition = simulation.LauncherLocalPosition;
        }
    }
}
