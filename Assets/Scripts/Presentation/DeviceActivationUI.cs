using System.Collections.Generic;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarblesECS.Presentation
{
    /// <summary>Routes pointer clicks to devices with OnActivate.</summary>
    [DisallowMultipleComponent]
    public sealed class DeviceActivationUI : MonoBehaviour
    {
        public MarbleGameController Controller;
        public Camera ViewCamera;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
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
    }
}
