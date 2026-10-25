using System.Collections.Generic;
using MarblesECS;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class DevicePlacementUI
{
    public bool IsDraggingInventory { get; private set; }
    private Vector2 dragPointer;
    private readonly List<RaycastResult> offerUiHits = new List<RaycastResult>();

    public bool BeginInventoryDrag(int instanceId, Vector2 pointer)
    {
        if (Controller == null || Controller.Paused || !TryGetDevice(instanceId, out var device) || device.Placed) return false;
        BeginPlacement(instanceId);
        IsDraggingInventory = SelectedInstanceId == instanceId;
        UpdateInventoryDrag(pointer);
        return IsDraggingInventory;
    }
    public void UpdateInventoryDrag(Vector2 pointer)
    {
        if (!IsDraggingInventory) return;
        dragPointer = pointer;
        LastFeedback = TryFindOfferTarget(pointer, out var target)
            ? "松开投入「" + target.Name + "」，消耗这一件库存装置。"
            : "拖到空白棋盘放置，或拖到可接收装置上投入；右键或 Esc 取消。";
    }
    public bool EndInventoryDrag(Vector2 pointer, GameObject pointerTarget = null)
    {
        if (!IsDraggingInventory) return false;
        IsDraggingInventory = false;
        if (!CanEdit || !TryGetSelectedDevice(out _))
        { LastFeedback = "已取消拖放，装置保留在背包。"; CancelPlacement(); return false; }
        // Dropping over the sale area only selects the sale; a separate left click must confirm it.
        if (SellArea && SellArea.ContainsPointer(pointer))
        { if (previewObject) previewObject.SetActive(false); LastFeedback = "左键点击红色区域确认出售，右键取消。"; return false; }
        if (IsControl(pointerTarget) || PointerOnControl(pointer))
        { LastFeedback = "已取消拖放，装置保留在背包。"; CancelPlacement(); return false; }
        if (TryOfferSelected(pointer, out bool accepted)) return accepted;
        bool placed = false;
        if (previewObject != null && TryGetBoardPoint(pointer, out var point))
        {
            placed = CommitSelected(point);
        }
        LastFeedback = placed ? "装置已放置。" : "此处不能放置，装置保留在背包。";
        CancelPlacement(); return placed;
    }
    private bool TryOfferSelected(Vector2 pointer, out bool accepted)
    {
        accepted = false;
        if (!TryFindOfferTarget(pointer, out var target)) return false;
        accepted = Controller.TryOfferDevice(SelectedInstanceId, target.InstanceId);
        LastFeedback = accepted ? "已投入「" + target.Name + "」。\n" + Controller.GetDeviceStatus(target.InstanceId)
            : "目标未接受，装置保留在背包。";
        CancelPlacement(); return true;
    }
    private bool TryFindOfferTarget(Vector2 pointer, out OwnedDeviceSnapshot target)
    {
        target = default;
        if (SelectedInstanceId == 0 || Controller == null || !Controller.IsReady || Controller.Paused || BoardRoot == null) return false;
        if (ViewCamera == null) ViewCamera = Camera.main;
        if (ViewCamera == null || !TryGetBoardPoint(pointer, out var point)) return false;
        float nearest = float.PositiveInfinity;
        foreach (var device in Controller.GetOwnedDevices())
        {
            if (!device.Placed || device.InstanceId == SelectedInstanceId) continue;
            float distance = new Vector2(point.x - device.X, point.z - device.Z).sqrMagnitude;
            if (distance > device.Radius * device.Radius || distance >= nearest || !Controller.CanOfferDevice(SelectedInstanceId, device.InstanceId)) continue;
            nearest = distance; target = device;
        }
        return target.InstanceId != 0;
    }
    private bool PointerOnControl(Vector2 position)
    {
        if (EventSystem.current == null) return false;
        offerUiHits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, offerUiHits);
        foreach (var hit in offerUiHits)
            if (IsControl(hit.gameObject)) return true;
        return false;
    }
    private static bool IsControl(GameObject target) => target != null &&
        (target.GetComponentInParent<UnityEngine.UI.Selectable>() != null ||
         ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) != null ||
         ExecuteEvents.GetEventHandler<IBeginDragHandler>(target) != null);
}
