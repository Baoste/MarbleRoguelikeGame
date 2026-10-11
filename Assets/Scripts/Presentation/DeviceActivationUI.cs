using System.Collections.Generic;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarblesECS.Presentation
{
    /// <summary>Generic interaction for devices with OnActivate; all availability and status text come from Lua.</summary>
    [DisallowMultipleComponent]
    public sealed class DeviceActivationUI : MonoBehaviour
    {
        public MarbleGameController Controller;
        public Camera ViewCamera;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private GUIStyle statusStyle;
        private bool Running => Controller != null && Controller.IsReady &&
            (Controller.Snapshot.Phase == RoundPhase.Playing || Controller.Snapshot.Phase == RoundPhase.Draining);

        private Camera ResolveCamera()
        {
            if (ViewCamera != null) return ViewCamera;
            var placement = FindObjectOfType<DevicePlacementUI>();
            if (placement != null && placement.Controller == Controller) ViewCamera = placement.ViewCamera;
            if (ViewCamera == null) ViewCamera = Camera.main;
            return ViewCamera;
        }
        private bool PointerBlocked(Vector2 position)
        {
            var hud = FindObjectOfType<MarbleGameHud>();
            if (hud != null && hud.isActiveAndEnabled && MarbleGameHud.ContainsPointer(position)) return true;
            var events = EventSystem.current;
            if (events == null) return false;
            uiHits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = position }, uiHits);
            foreach (var hit in uiHits)
            {
                // Decorative full-screen graphics are not controls; buttons, drag targets and scroll areas are.
                if (hit.gameObject.GetComponentInParent<Selectable>() != null ||
                    ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject) != null ||
                    ExecuteEvents.GetEventHandler<IBeginDragHandler>(hit.gameObject) != null) return true;
            }
            return false;
        }
        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!Running || Controller.Paused || !Input.GetMouseButtonDown(0)) return;
            var camera = ResolveCamera();
            if (camera != null && !PointerBlocked(Input.mousePosition))
                Controller.TryActivateDevice(camera.ScreenPointToRay(Input.mousePosition));
#endif
        }
        private void OnGUI()
        {
            if (!Running) return;
            var camera = ResolveCamera();
            if (camera == null) return;
            if (statusStyle == null) statusStyle = new GUIStyle(GUI.skin.box) { richText = false, fontSize = 14 };
            var owned = new Dictionary<int, OwnedDeviceSnapshot>();
            foreach (var item in Controller.GetOwnedDevices()) owned[item.InstanceId] = item;
            foreach (var device in MResDeviceBody.Active)
            {
                if (device == null || !device.isActiveAndEnabled || Controller.PhysicsRoot == null ||
                    device.gameObject.scene != Controller.PhysicsRoot.gameObject.scene) continue;
                string status = Controller.GetDeviceStatus(device.InstanceId);
                if (!owned.TryGetValue(device.InstanceId, out var snapshot)) continue;
                status = DeviceDisplayText.Durability(snapshot) +
                    (snapshot.Exhausted || string.IsNullOrEmpty(status) ? "" : "\n" + status);
                Vector3 point = camera.WorldToScreenPoint(device.transform.position);
                if (point.z <= 0) continue;
                var content = new GUIContent(status);
                Vector2 size = statusStyle.CalcSize(content);
                GUI.Box(new Rect(point.x - size.x * .5f, Screen.height - point.y - size.y - 24, size.x, size.y), content, statusStyle);
            }
        }
    }
}
