using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        public Vector3 LauncherLocalPosition => IsReady ? simulation.LauncherLocalPosition : Vector3.zero;
        public float LauncherCenterZ => IsReady ? simulation.LauncherCenterZ : (LaunchPoint != null ? LaunchPoint.localPosition.z : 0f);

        private Vector3 LaunchWorldPosition => LaunchPoint.TransformPoint(LaunchOffset);

        private void OnDrawGizmos()
        {
            if (!ShowLaunchGizmos || LaunchPoint == null || !SimulationContext.IsFinite(LaunchOffset)) return;
            Vector3 position = LaunchWorldPosition;
            Quaternion rotation = LaunchPoint.rotation;
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            try
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(LaunchPoint.position, position);
                Gizmos.DrawWireSphere(LaunchPoint.position, .035f);
                Gizmos.color = Color.green;
                // This marker locates the spawn center; its size does not configure the marble radius.
                Gizmos.DrawWireSphere(position, .1f);
                Gizmos.color = Color.cyan;
                Vector3 tip = position + rotation * Vector3.down * .5f;
                Gizmos.DrawLine(position, tip);
                Gizmos.DrawLine(tip, tip + rotation * new Vector3(-.08f, .12f, 0));
                Gizmos.DrawLine(tip, tip + rotation * new Vector3(.08f, .12f, 0));
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
            }
        }

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
