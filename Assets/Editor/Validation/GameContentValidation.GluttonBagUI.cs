using System;
using System.Linq;
using System.Reflection;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void BagOfferUi()
        {
            using (var f = new Fixture())
            {
                var bag = AddGluttonBag(f); BagBuild(f.C);
                var root = new GameObject("Offer UI test board");
                var controlObject = new GameObject("Offer controller");
                var cameraObject = new GameObject("Offer camera");
                var cardObject = new GameObject("Inventory card", typeof(RectTransform), typeof(UnityEngine.UI.Button));
                var eventsObject = new GameObject("Offer events", typeof(EventSystem));
                var controller = controlObject.AddComponent<MarbleGameController>(); controller.enabled = false;
                var simulationField = typeof(MarbleGameController).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic);
                simulationField.SetValue(controller, f.Simulation);
                controller.PhysicsRoot = root.transform;
                var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5;
                camera.transform.SetPositionAndRotation(new Vector3(0, 10, 0), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                var placement = controlObject.AddComponent<DevicePlacementUI>(); placement.Controller = controller;
                placement.BoardRoot = root.transform; placement.PlacementPlane = root.transform; placement.ViewCamera = camera;
                var card = cardObject.AddComponent<OwnedDeviceButtonUI>();
                GameObject blockingCanvas = null, rigObject = null;
                try
                {
                    int source = BagFood(f.C);
                    card.Bind(f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == source), true, placement.BeginPlacement, placement);
                    Vector2 pointer = camera.WorldToScreenPoint(Vector3.zero);
                    var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = pointer };
                    card.OnPointerDown(data); card.OnBeginDrag(data);
                    Check(placement.IsDraggingInventory && placement.SelectedInstanceId == source, "inventory drag captures actual instance identity");
                    card.OnDrag(data); Check(placement.LastFeedback.Contains("松开投入"), "drag announces inventory consumption before release");
                    card.OnEndDrag(data);
                    Check(!placement.IsDraggingInventory && placement.SelectedInstanceId == 0, "accepted drop clears ghost and selection");
                    Check(!CampaignStateUtility.FindDevice(f.C, source, out _), "drag release reaches real simulation offer");
                    Check(placement.LastFeedback.Contains("成长 +2"), "drop feedback exposes accepted growth");
                    card.SelectButton.onClick.Invoke(); Near(0, placement.SelectedInstanceId, "synthetic click after drag does not select a shifted inventory slot");
                    source = BagFood(f.C);
                    Check(placement.BeginInventoryDrag(source, pointer), "begin cancellation test"); placement.CancelPlacement();
                    Check(!placement.EndInventoryDrag(pointer) && CampaignStateUtility.FindDevice(f.C, source, out _), "cancel then release does not consume");
                    controller.SetPaused(true); Check(!placement.BeginInventoryDrag(source, pointer), "pause rejects beginning inventory drag"); controller.SetPaused(false);
                    Check(placement.BeginInventoryDrag(source, pointer), "begin off-board drop");
                    Check(!placement.EndInventoryDrag(camera.WorldToScreenPoint(new Vector3(50, 0, 50))), "outside-plane drop fails");
                    Check(CampaignStateUtility.FindDevice(f.C, source, out _), "outside-plane drop retains inventory");
                    blockingCanvas = new GameObject("Blocking UI", typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
                    blockingCanvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                    var control = new GameObject("Control", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
                    control.transform.SetParent(blockingCanvas.transform, false);
                    var rect = (RectTransform)control.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero; Canvas.ForceUpdateCanvases();
                    Check(placement.BeginInventoryDrag(source, pointer), "begin obstructed drop");
                    // Headless canvas depth is -1; supply the pointer event's control hit directly.
                    card.Bind(f.Simulation.GetOwnedDevices().Single(d => d.InstanceId == source), true, placement.BeginPlacement, placement);
                    card.OnPointerDown(data); card.OnBeginDrag(data);
                    data.pointerCurrentRaycast = new RaycastResult { gameObject = control };
                    card.OnEndDrag(data);
                    Check(!placement.IsDraggingInventory && CampaignStateUtility.FindDevice(f.C, source, out _), "UI control hit blocks feeding through it");
                    data.pointerCurrentRaycast = default;
                    UnityEngine.Object.DestroyImmediate(blockingCanvas); blockingCanvas = null;
                    Check(placement.BeginInventoryDrag(source, pointer), "begin phase-transition test");
                    Check(f.Simulation.BeginRound(), "round can start while stale UI still has a drag");
                    Check(!placement.EndInventoryDrag(pointer) && CampaignStateUtility.FindDevice(f.C, source, out _), "release after leaving Build cannot consume inventory");
                    BagBuild(f.C);
                    // Exercise the active diegetic card's real pointer handlers as well as the legacy inventory button.
                    rigObject = new GameObject("Diegetic offer test"); rigObject.SetActive(false);
                    var rig = rigObject.AddComponent<DiegeticMachineUI>(); rig.Controller = controller; rig.Placement = placement;
                    typeof(DiegeticMachineUI).GetField("slide", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(rig, 1f);
                    var ids = (int[])typeof(DiegeticMachineUI).GetField("deviceIds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig); ids[0] = source;
                    var hit = cardObject.AddComponent<DiegeticUIHit>(); hit.Owner = rig; hit.Kind = 2; hit.Index = 0;
                    hit.OnPointerDown(data); hit.OnBeginDrag(data); hit.OnDrag(data);
                    Check(placement.IsDraggingInventory && placement.SelectedInstanceId == source, "diegetic card begins real inventory drag");
                    int replacement = BagFood(f.C); ids[0] = replacement; // Pagination cannot change the captured source.
                    hit.OnEndDrag(data);
                    Check(!placement.IsDraggingInventory && placement.LastFeedback.Contains("已投入"), "diegetic pointer release submits the offer");
                    Check(!CampaignStateUtility.FindDevice(f.C, source, out _) && CampaignStateUtility.FindDevice(f.C, replacement, out _), "slot changes never consume the replacement item");
                    hit.OnPointerClick(data); Near(0, placement.SelectedInstanceId, "diegetic post-drag click does not select replacement");
                }
                finally
                {
                    if (blockingCanvas != null) UnityEngine.Object.DestroyImmediate(blockingCanvas);
                    if (rigObject != null) UnityEngine.Object.DestroyImmediate(rigObject);
                    UnityEngine.Object.DestroyImmediate(cardObject);
                    simulationField.SetValue(controller, null);
                    UnityEngine.Object.DestroyImmediate(controlObject); UnityEngine.Object.DestroyImmediate(cameraObject);
                    UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(eventsObject);
                }
            }
        }
    }
}
