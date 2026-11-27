using System;
using System.Collections.Generic;
using MarblesECS;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class DevicePlacementUI
{
    private enum DropTarget { Blocked, Board, Backpack, Sale, Offer }

    public bool IsDraggingInventory => IsDraggingDevice && !IsMovingPlacedDevice;
    internal bool IsDraggingDevice => SelectedInstanceId != 0 || SelectedDefinitionId != 0;
    internal event Action DeviceDropFinished;
    private Vector2 dragPointer;
    private readonly List<RaycastResult> offerUiHits = new List<RaycastResult>();
    private EventSystem pointerEventSystem;
    private PointerEventData pointerEvent;

    public bool BeginInventoryDrag(int instanceId, Vector2 pointer)
    {
        if (!CanEdit || IsDraggingDevice || !TryGetDevice(instanceId, out var device) || device.Placed) return false;
        BeginDrag(device);
        UpdateInventoryDrag(pointer);
        return true;
    }
    public void UpdateInventoryDrag(Vector2 pointer)
    {
        if (!IsDraggingInventory) return;
        dragPointer = pointer;
        UpdatePlacementPointer(pointer);
    }
    public bool EndInventoryDrag(Vector2 pointer, GameObject pointerTarget = null) =>
        IsDraggingInventory && FinishDrag(pointer, pointerTarget);

    private DropTarget FindDropTarget(Vector2 pointer, GameObject pointerTarget, out OwnedDeviceSnapshot receiver)
    {
        receiver = default;
        if (SellArea && SellArea.ContainsPointer(pointer)) return DropTarget.Sale;
        if (PointerOnBackpack(pointer))
            return Controller.Session.Production.AllowDeviceReturn ? DropTarget.Backpack : DropTarget.Blocked;
        if (IsControl(pointerTarget) || PointerOnControl(pointer)) return DropTarget.Blocked;
        return (!IsMovingPlacedDevice || BoardPointerMoved(pointer)) && TryFindOfferTarget(pointer, out receiver)
            ? DropTarget.Offer : DropTarget.Board;
    }

    private bool FinishDrag(Vector2 pointer, GameObject pointerTarget)
    {
        if (SelectedDefinitionId != 0) return FinishInstall(pointer, pointerTarget);
        if (!TryGetSelectedDevice(out var device))
        {
            CancelPlacement();
            return false;
        }

        bool committed;
        switch (FindDropTarget(pointer, pointerTarget, out var receiver))
        {
            case DropTarget.Sale:
                committed = Controller.SellDevice(device.InstanceId);
                LastFeedback = committed ? $"已出售{DeviceDisplayText.SaleTarget(device)}，获得 {device.SellPrice} 血肉残料。" : "出售未完成。";
                break;
            case DropTarget.Backpack:
                committed = device.Placed && Controller.RemoveDevicePlacement(device.InstanceId);
                LastFeedback = device.Placed ? (committed ? "已收回背包。" : "收回未完成。") : "已放回背包。";
                break;
            case DropTarget.Offer:
                committed = Controller.TryOfferDevice(device.InstanceId, receiver.InstanceId);
                LastFeedback = committed ? $"已投入「{receiver.Name}」。\n{Controller.GetDeviceStatus(receiver.InstanceId)}"
                    : "目标未接受，装置已退回原处。";
                break;
            case DropTarget.Board:
                committed = TryGetBoardPoint(pointer, out var point) && CommitSelected(point);
                LastFeedback = committed ? string.Empty : "此处不能放置，装置已退回原处。";
                break;
            default:
                committed = false;
                LastFeedback = "已取消拖放，装置已退回原处。";
                break;
        }
        if (committed) SyncPlacedObjects();
        CancelPlacement();
        DeviceDropFinished?.Invoke();
        return committed;
    }

    private bool TryFindOfferTarget(Vector2 pointer, out OwnedDeviceSnapshot target)
    {
        target = default;
        if (!CanEdit || SelectedInstanceId == 0) return false;
        if (ViewCamera == null) ViewCamera = Camera.main;
        if (ViewCamera == null || !TryPickPlaced(pointer, out var view) ||
            !Controller.CanOfferDevice(SelectedInstanceId, view.Device.InstanceId)) return false;
        target = view.Device;
        return true;
    }
    private bool PointerOnControl(Vector2 position, bool includePanels = false)
    {
        var events = EventSystem.current;
        if (events == null) return false;
        if (pointerEventSystem != events)
        {
            pointerEventSystem = events;
            pointerEvent = new PointerEventData(events);
        }
        pointerEvent.position = position;
        offerUiHits.Clear();
        events.RaycastAll(pointerEvent, offerUiHits);
        foreach (var hit in offerUiHits)
        {
            var target = hit.gameObject;
            if (!target) continue;
            if (IsControl(target) || (includePanels && target.TryGetComponent<CanvasGroup>(out var panel) &&
                panel.blocksRaycasts && panel.alpha > 0f)) return true;
        }
        return false;
    }
    private static bool IsControl(GameObject target) => target != null &&
        (target.GetComponentInParent<UnityEngine.UI.Selectable>() != null ||
         ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) != null ||
         ExecuteEvents.GetEventHandler<IBeginDragHandler>(target) != null);
}
