using MarblesECS;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class DevicePlacementUI
{
    private static readonly Color ValidOutline = new Color(0.2f, 1f, 0.35f);
    private static readonly Color InvalidOutline = new Color(1f, 0.15f, 0.12f);

    public bool IsMovingPlacedDevice { get; private set; }
    public bool CanEdit => isActiveAndEnabled && Controller != null && Controller.IsReady &&
        !Controller.Paused && Controller.ProductionCommandsAvailable && Controller.HasProductionFeature(ProductionFeature.DevicePlacement) &&
        Controller.Session.Phase == RoundPhase.Build && BoardRoot != null;
    internal CanvasGroup BackpackDropArea { get; set; }
    private GameObject hiddenPlacedObject;
    private Vector2 boardPressPosition;
    private float previewAngle;
    private int selectionRound;

    private void BeginDrag(OwnedDeviceSnapshot device)
    {
        ClearPlacedDeviceHover();
        SyncPlacedObjects();
        preview = MResDeviceView.Create(device, BoardRoot, null, true);
        SelectedInstanceId = device.InstanceId;
        selectionRound = Controller.Snapshot.RoundId;
        previewAngle = device.Angle;
        IsMovingPlacedDevice = device.Placed;
        HideSelectedView();
        LastFeedback = string.Empty;
    }

    public bool TryGetSelectedDevice(out OwnedDeviceSnapshot device)
    {
        device = default;
        return CanEdit && SelectedInstanceId != 0 && selectionRound == Controller.Snapshot.RoundId &&
            TryGetDevice(SelectedInstanceId, out device) && device.Placed == IsMovingPlacedDevice;
    }

    private void ValidateSelection()
    {
        if (SelectedInstanceId != 0 && !TryGetSelectedDevice(out _)) CancelPlacement();
    }

    private void HideSelectedView()
    {
        if (!IsMovingPlacedDevice || SelectedInstanceId == 0) return;
        hiddenPlacedObject = GetPlacedDeviceRoot(SelectedInstanceId);
        if (!hiddenPlacedObject) return;
        // Keep the committed placement and hide its view while dragging.
        hiddenPlacedObject.SetActive(false);
    }

    private void RestoreSelectedView()
    {
        if (hiddenPlacedObject) hiddenPlacedObject.SetActive(true);
        hiddenPlacedObject = null;
    }

    public void RotateSelected(float degrees)
    {
        if (!TryGetSelectedDevice(out var device) || device.RotationLocked || !MarbleRules.IsFinite(degrees)) return;
        previewAngle = (previewAngle + degrees) % 360;
    }

    public void UpdatePlacementPointer(Vector2 pointer)
    {
        ValidateSelection();
        if (!previewObject) return;
        if (!ViewCamera) ViewCamera = Camera.main;
        var drop = FindDropTarget(pointer, null, out var receiver);
        if (drop == DropTarget.Sale || drop == DropTarget.Backpack)
        {
            if (drop == DropTarget.Sale && TryGetSelectedDevice(out var device))
                LastFeedback = $"松开出售「{device.Name}」，获得 {device.SellPrice} 金币。";
            else
                LastFeedback = IsMovingPlacedDevice ? "松开收回背包" : "松开放回背包";
            previewObject.SetActive(false);
            return;
        }

        LastFeedback = drop == DropTarget.Offer ? $"松开投入「{receiver.Name}」，会消耗这件装置。" : string.Empty;
        if (!TryProjectOntoPlacementPlane(pointer, out var worldPoint))
        {
            previewObject.SetActive(false);
            return;
        }
        var point = BoardRoot.InverseTransformPoint(worldPoint);
        Transform pose = SetPreviewPose(ref point);
        bool canPlace = drop == DropTarget.Board && IsInsidePlacementPlane(worldPoint) && IsInsidePlacementPlane(pose.position) &&
            Controller.CanPlaceDevice(SelectedInstanceId, point.x, point.z, pose.position, PlacementRadiusScale, previewAngle, pose.rotation);
        previewOutline ??= new QuickOutline(previewObject);
        previewOutline.SetColor(canPlace || drop == DropTarget.Offer ? ValidOutline : InvalidOutline);
        previewObject.SetActive(true);
    }

    private Transform SetPreviewPose(ref Vector3 point)
    {
        SnapPoint(ref point);
        var pose = previewObject.transform;
        SetDeviceTransform(pose, point.x, point.z);
        pose.rotation *= Quaternion.Euler(0, previewAngle, 0);
        return pose;
    }

    private void SnapPoint(ref Vector3 point)
    {
        if (!SnapToGrid || Controller.GridSize <= 0) return;
        point.x = Mathf.Round(point.x / Controller.GridSize) * Controller.GridSize;
        point.z = Mathf.Round(point.z / Controller.GridSize) * Controller.GridSize;
    }

    internal bool HandleBoardPointerDown(Vector2 pointer)
    {
        if (!CanEdit || IsDraggingDevice || (SellArea && SellArea.ContainsPointer(pointer)) ||
            PointerOnBackpack(pointer) || PointerOnControl(pointer)) return false;
        if (!ViewCamera) ViewCamera = Camera.main;
        if (!ViewCamera) return false;
        SyncPlacedObjects();
        if (!TryPickPlaced(pointer, out var view) || !TryGetDevice(view.Device.InstanceId, out var device) || !device.Placed)
            return false;
        boardPressPosition = pointer;
        BeginDrag(device);
        return true;
    }

    internal bool HandleBoardPointerUp(Vector2 pointer)
    {
        if (!IsMovingPlacedDevice) return false;
        if (BoardPointerMoved(pointer)) return FinishDrag(pointer, null);
        CancelPlacement();
        return false;
    }

    private bool BoardPointerMoved(Vector2 pointer)
    {
        var events = EventSystem.current;
        float threshold = events ? Mathf.Max(1, events.pixelDragThreshold) : 5;
        return (pointer - boardPressPosition).sqrMagnitude >= threshold * threshold;
    }

    private bool PointerOnBackpack(Vector2 pointer)
    {
        if (!BackpackDropArea || !BackpackDropArea.isActiveAndEnabled || !BackpackDropArea.interactable ||
            !BackpackDropArea.blocksRaycasts || BackpackDropArea.alpha <= 0f ||
            !(BackpackDropArea.transform is RectTransform rect)) return false;
        var canvas = BackpackDropArea.GetComponentInParent<Canvas>();
        if (!canvas || !canvas.isActiveAndEnabled) return false;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, pointer, camera);
    }

    private bool CommitSelected(Vector3 point)
    {
        if (!TryGetSelectedDevice(out _) || !previewObject) return false;
        Transform pose = SetPreviewPose(ref point);
        return IsInsidePlacementPlane(pose.position) && Controller.PlaceDevice(SelectedInstanceId,
            point.x, point.z, pose.position, PlacementRadiusScale, previewAngle, pose.rotation);
    }

    private float PlacementRadiusScale
    {
        get
        {
            // Packaged footprints already include the device's authored scale.
            var scale = BoardRoot.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
    }

}
