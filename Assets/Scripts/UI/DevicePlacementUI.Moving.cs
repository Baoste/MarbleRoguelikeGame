using MarblesECS;
using MarblesECS.Presentation;
using UnityEngine;

public sealed partial class DevicePlacementUI
{
    public bool IsMovingPlacedDevice { get; private set; }
    public bool CanEdit => isActiveAndEnabled && Controller != null && Controller.IsReady &&
        !Controller.Paused && Controller.Session.Phase == RoundPhase.Build && BoardRoot != null;
    private GameObject hiddenPlacedObject;
    private float previewAngle;
    private int selectionRound;

    private bool BeginSelection(int id)
    {
        if (!CanEdit || !TryGetDevice(id, out var device)) return false;
        CancelPlacement();
        SyncPlacedObjects();
        preview = MResDeviceView.Create(device, BoardRoot, null, true);
        SelectedInstanceId = id;
        selectionRound = Controller.Snapshot.RoundId;
        previewAngle = device.Angle;
        IsMovingPlacedDevice = device.Placed;
        HideSelectedView();
        LastFeedback = "左键确认位置，R 旋转，右键取消；移到下方红色区域可出售。";
        return true;
    }

    public bool TryGetSelectedDevice(out OwnedDeviceSnapshot device)
    {
        device = default;
        return CanEdit && SelectedInstanceId != 0 && selectionRound == Controller.Snapshot.RoundId &&
            TryGetDevice(SelectedInstanceId, out device);
    }

    private void ValidateSelection()
    {
        if (SelectedInstanceId != 0 && !TryGetSelectedDevice(out _)) CancelPlacement();
    }

    private void HideSelectedView()
    {
        if (!IsMovingPlacedDevice || SelectedInstanceId == 0 ||
            !placedObjects.TryGetValue(SelectedInstanceId, out var handle) || !handle.Root) return;
        hiddenPlacedObject = handle.Root;
        // Only hide the view. The instance, Lua state and original placement stay committed until confirmation.
        hiddenPlacedObject.SetActive(false);
    }

    private void RestoreSelectedView()
    {
        if (hiddenPlacedObject) hiddenPlacedObject.SetActive(true);
        hiddenPlacedObject = null;
    }

    public void RotateSelected(float degrees)
    {
        if (!TryGetSelectedDevice(out _) || !MarbleRules.IsFinite(degrees)) return;
        previewAngle = (previewAngle + degrees) % 360;
    }

    public void UpdatePlacementPointer(Vector2 pointer)
    {
        ValidateSelection();
        if (!TryGetSelectedDevice(out _) || !previewObject) return;
        if (!ViewCamera) ViewCamera = Camera.main;
        if (!ViewCamera || (SellArea && SellArea.ContainsPointer(pointer)) || !TryGetBoardPoint(pointer, out var point))
        { previewObject.SetActive(false); return; }
        SnapPoint(ref point);
        SetDeviceTransform(previewObject.transform, point.x, point.z);
        previewObject.transform.rotation *= Quaternion.Euler(0, previewAngle, 0);
        previewObject.SetActive(true);
    }

    private void SnapPoint(ref Vector3 point)
    {
        if (!SnapToGrid || Controller.GridSize <= 0) return;
        point.x = Mathf.Round(point.x / Controller.GridSize) * Controller.GridSize;
        point.z = Mathf.Round(point.z / Controller.GridSize) * Controller.GridSize;
    }

    public bool HandleBoardClick(Vector2 pointer)
    {
        if (!CanEdit || IsDraggingInventory || (SellArea && SellArea.ContainsPointer(pointer)) || PointerOnControl(pointer)) return false;
        if (!ViewCamera) ViewCamera = Camera.main;
        if (!ViewCamera) return false;
        ValidateSelection();
        if (SelectedInstanceId == 0)
        {
            SyncPlacedObjects();
            return TryPickPlaced(pointer, out int id) && BeginSelection(id);
        }
        if (!TryGetBoardPoint(pointer, out var point))
        { LastFeedback = "请在盘面空位左键放置，或移到红色区域出售；右键取消。"; return false; }
        bool placed = CommitSelected(point);
        LastFeedback = placed ? "装置已放置。" : "此处不能放置，请避开边缘、固定障碍和其他装置；右键取消。";
        if (placed) { CancelPlacement(); SyncPlacedObjects(); }
        return placed;
    }

    private bool CommitSelected(Vector3 point)
    {
        if (!TryGetSelectedDevice(out _) || !previewObject) return false;
        SnapPoint(ref point);
        SetDeviceTransform(previewObject.transform, point.x, point.z);
        previewObject.transform.rotation *= Quaternion.Euler(0, previewAngle, 0);
        var scale = previewObject.transform.lossyScale;
        return IsInsidePlacementPlane(previewObject.transform.position) && Controller.PlaceDevice(SelectedInstanceId,
            point.x, point.z, previewObject.transform.position, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)), previewAngle);
    }

    public bool TrySellSelected()
    {
        if (IsDraggingInventory || !TryGetSelectedDevice(out var device)) return false;
        if (!Controller.SellDevice(device.InstanceId)) return false;
        LastFeedback = "已出售「" + device.Name + "」，获得 " + device.SellPrice + " 金币。";
        CancelPlacement();
        SyncPlacedObjects();
        return true;
    }

    private bool TryPickPlaced(Vector2 pointer, out int id)
    {
        id = 0;
        var ray = ViewCamera.ScreenPointToRay(pointer);
        float nearest = float.PositiveInfinity;
        foreach (var item in placedObjects)
        {
            if (!item.Value.Root) continue;
            foreach (var collider in item.Value.Root.GetComponentsInChildren<Collider>())
                if (collider.enabled && collider.Raycast(ray, out var hit, nearest))
                { id = item.Key; nearest = hit.distance; }
        }
        if (id != 0) return true;
        // Exhausted devices can have disabled colliders; their logical footprint remains selectable in Build.
        if (!TryGetBoardPoint(pointer, out var point)) return false;
        foreach (var device in Controller.GetOwnedDevices())
        {
            if (!device.Placed) continue;
            float distance = new Vector2(point.x - device.X, point.z - device.Z).sqrMagnitude;
            if (distance <= device.Radius * device.Radius && distance < nearest)
            { id = device.InstanceId; nearest = distance; }
        }
        return id != 0;
    }
}
