using System.Collections.Generic;
using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    /// <summary>Projects owned ECS devices into collision geometry; requests edits through commands.</summary>
    public sealed class MarbleBoardView : MonoBehaviour
    {
        public Camera ViewCamera;
        public int SelectedDeviceId { get; private set; }
        public string Feedback { get; private set; } = "选择库存中的钉子，再点击盘面放置。";
        private MarbleGameController controller;
        private Transform board;
        private MarbleVisualPalette palette;
        private int revision = -1;
        private int roundId = -1;
        private readonly Dictionary<int, GameObject> devices = new Dictionary<int, GameObject>();

        internal void Initialize(MarbleGameController game, Transform boardRoot, MarbleVisualPalette looks)
        { controller = game; board = boardRoot; palette = looks; }

        public void Select(int id)
        {
            SelectedDeviceId = id;
            Feedback = "点击空白盘面放置或移动；点击已有钉子可选中。";
        }

        private void LateUpdate()
        {
            if (controller == null || !controller.IsReady) return;
            if (revision == controller.Session.LayoutRevision && roundId == controller.Snapshot.RoundId) return;
            revision = controller.Session.LayoutRevision;
            roundId = controller.Snapshot.RoundId;
            foreach (GameObject item in devices.Values)
            { item.SetActive(false); Destroy(item); }
            devices.Clear();
            bool selectedExists = false;
            foreach (var owned in controller.GetOwnedDevices())
            {
                if (owned.InstanceId == SelectedDeviceId) selectedExists = true;
                if (!owned.Placed) continue;
                float height = controller.Balance.Board.PinHeight;
                GameObject obj = MarbleBoardGeometry.Primitive(owned.Name + " #" + owned.InstanceId,
                    PrimitiveType.Cylinder, board, new Vector3(owned.X, height / 2, owned.Z),
                    new Vector3(owned.Radius * 2, height / 2, owned.Radius * 2),
                    owned.RushChanceAdd > 0 ? palette.Rush : palette.Multiplier);
                var pin = obj.AddComponent<MarblePin>();
                pin.TargetId = checked(100000 + owned.InstanceId);
                pin.ScoreMultiplier = (float)owned.ScoreMultiplier;
                pin.RushChanceAdd = (float)owned.RushChanceAdd;
                devices.Add(owned.InstanceId, obj);
            }
            if (!selectedExists) SelectedDeviceId = 0;
            Physics.SyncTransforms();
        }

        private void OnGUI()
        {
            if (controller == null || !controller.IsReady || controller.Session.Phase != RoundPhase.Build) return;
            if (ViewCamera == null) ViewCamera = Camera.main;
            if (ViewCamera == null) return;
            Event input = Event.current;
            if (input.type == EventType.MouseDown && input.button == 1)
            { SelectedDeviceId = 0; Feedback = "已取消选择。"; }
            if (input.type == EventType.MouseDown && input.button == 0 &&
                !MarbleGameHud.ContainsPointer(input.mousePosition) && TryBoardPoint(input.mousePosition, out Vector3 point))
            {
                HandleClick(point);
                input.Use();
            }
            if (devices.TryGetValue(SelectedDeviceId, out GameObject selected))
            {
                Vector3 screen = ViewCamera.WorldToScreenPoint(selected.transform.position);
                Color previous = GUI.color;
                GUI.color = Color.yellow;
                GUI.Label(new Rect(screen.x - 12, Screen.height - screen.y - 38, 100, 25), "▼ 已选中");
                GUI.color = previous;
            }
        }

        private bool TryBoardPoint(Vector2 guiPoint, out Vector3 point)
        {
            Ray ray = ViewCamera.ScreenPointToRay(new Vector3(guiPoint.x, Screen.height - guiPoint.y));
            if (new Plane(board.up, board.position).Raycast(ray, out float distance))
            { point = board.InverseTransformPoint(ray.GetPoint(distance)); return true; }
            point = default;
            return false;
        }

        private void HandleClick(Vector3 point)
        {
            foreach (var item in controller.GetOwnedDevices())
                if (item.Placed && Vector2.Distance(new Vector2(point.x, point.z), new Vector2(item.X, item.Z)) < item.Radius + .25f)
                { Select(item.InstanceId); return; }
            if (SelectedDeviceId == 0) { Feedback = "先在左侧库存选择一枚钉子。"; return; }
            float grid = controller.Balance.Board.GridSize;
            if (Event.current.shift && grid > 0)
            {
                point.x = Mathf.Round(point.x / grid) * grid;
                point.z = Mathf.Round(point.z / grid) * grid;
            }
            Feedback = controller.PlaceDevice(SelectedDeviceId, point.x, point.z)
                ? "布置成功。继续点击可移动；下一回合保留布局。"
                : "此处不能放置：请避开普通钉、其他装置、发射口和终点区域。";
        }
    }
}
