using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    /// <summary>Legacy board controls for the immediate-mode campaign HUD.</summary>
    public sealed class MarbleBoardView : MonoBehaviour
    {
        public Camera ViewCamera;
        public int SelectedDeviceId { get; private set; }
        public string Feedback { get; private set; } = "选择库存中的钉子，再点击盘面放置。";
        private MarbleGameController controller;
        private Transform Board => controller.DeviceOrigin;

        internal void Initialize(MarbleGameController game, Transform boardRoot, MarbleVisualPalette looks)
        {
            controller = game;
        }

        public void Select(int id)
        {
            SelectedDeviceId = id;
            Feedback = "点击空白盘面放置或移动；点击已有钉子可选中。";
        }

        private int roundId = -1;
        private int revision = -1;
        private void LateUpdate()
        {
            if (controller == null || !controller.IsReady) return;
            int currentRound = controller.Snapshot.RoundId;
            int currentRevision = controller.Session.LayoutRevision;
            if (roundId == currentRound && revision == currentRevision) return;
            if (roundId == currentRound && controller.HasProductionFeature(ProductionFeature.DevicePlacement))
                foreach (var device in controller.GetOwnedDevices())
                    if (device.InstanceId == SelectedDeviceId) { revision = currentRevision; return; }
            roundId = currentRound;
            revision = currentRevision;
            SelectedDeviceId = 0;
        }

        private void OnGUI()
        {
            if (controller == null || !controller.ProductionCommandsAvailable ||
                !controller.HasProductionFeature(ProductionFeature.DevicePlacement) || controller.Session.Phase != RoundPhase.Build) return;
            if (ViewCamera == null) ViewCamera = Camera.main;
            if (ViewCamera == null) return;
            Event input = Event.current;
            if (input.type == EventType.KeyDown && input.keyCode == KeyCode.R && SelectedDeviceId != 0)
            { controller.RotateDevice(SelectedDeviceId, 45); input.Use(); }
            if (input.type == EventType.MouseDown && input.button == 1)
            { SelectedDeviceId = 0; Feedback = "已取消选择。"; }
            if (input.type == EventType.MouseDown && input.button == 0 &&
                !MarbleGameHud.ContainsPointer(input.mousePosition) && TryBoardPoint(input.mousePosition, out Vector3 point))
            {
                HandleClick(point);
                input.Use();
            }
            if (controller.TryGetDevicePose(SelectedDeviceId, out var pose))
            {
                Vector3 screen = ViewCamera.WorldToScreenPoint(pose.Position);
                Color previous = GUI.color;
                GUI.color = Color.yellow;
                GUI.Label(new Rect(screen.x - 12, Screen.height - screen.y - 38, 100, 25), "▼ 已选中");
                GUI.color = previous;
            }
        }

        private bool TryBoardPoint(Vector2 guiPoint, out Vector3 point)
        {
            Ray ray = ViewCamera.ScreenPointToRay(new Vector3(guiPoint.x, Screen.height - guiPoint.y));
            var plane = controller.DevicePlane;
            if (plane != null && new Plane(plane.up, plane.position).Raycast(ray, out float distance))
            { point = Board.InverseTransformPoint(ray.GetPoint(distance)); return true; }
            point = default;
            return false;
        }

        private void HandleClick(Vector3 point)
        {
            foreach (var item in controller.GetOwnedDevices())
                if (item.Placed && Vector2.Distance(new Vector2(point.x, point.z), new Vector2(item.PlacementX, item.PlacementZ)) < item.Radius + .25f)
                { Select(item.InstanceId); return; }
            if (SelectedDeviceId == 0) { Feedback = "先在左侧库存选择一枚钉子。"; return; }
            float grid = controller.GridSize;
            if (Event.current.shift && grid > 0)
            {
                point.x = Mathf.Round(point.x / grid) * grid;
                point.z = Mathf.Round(point.z / grid) * grid;
            }
            Feedback = controller.PlaceDevice(SelectedDeviceId, point.x, point.z)
                ? "布置成功。继续点击可移动。"
                : "此处不能放置：请避开普通钉、其他装置、发射口和终点区域。";
        }
    }
}
