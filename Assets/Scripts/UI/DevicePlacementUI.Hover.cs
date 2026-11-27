using System;
using MarblesECS;
using MarblesECS.Presentation;
using UnityEngine;

public sealed partial class DevicePlacementUI
{
    private sealed class PlacedDeviceView : IDisposable
    {
        internal OwnedDeviceSnapshot Device;
        internal QuickOutline Outline;
        internal GameObject OutlinedModel;
        public void Dispose() => Outline?.Dispose();
    }

    private PlacedDeviceView hoveredView;
    private OwnedDeviceSnapshot hoveredDevice;
    private float nextHoverRefresh;

    internal GameObject GetPlacedDeviceRoot(int instanceId) =>
        Controller != null ? Controller.GetDeviceModel(instanceId) : null;

    internal bool UpdatePlacedDeviceHover(Vector2 pointer, out OwnedDeviceSnapshot device)
    {
        device = default;
        if (!isActiveAndEnabled || !Controller || !Controller.IsReady || !BoardRoot ||
            IsDraggingDevice || placedObjects.Count == 0)
        {
            ClearPlacedDeviceHover();
            return false;
        }

        if (!ViewCamera) ViewCamera = Camera.main;
        if (!ViewCamera || !ViewCamera.pixelRect.Contains(pointer) ||
            !TryPickPlaced(pointer, out var view) || PointerOnControl(pointer, includePanels: true))
        {
            ClearPlacedDeviceHover();
            return false;
        }

        if (view != hoveredView)
        {
            ClearPlacedDeviceHover();
            hoveredView = view;
        }

        if (Time.unscaledTime >= nextHoverRefresh)
        {
            if (!TryGetDevice(view.Device.InstanceId, out hoveredDevice) || !hoveredDevice.Placed)
            {
                ClearPlacedDeviceHover();
                return false;
            }
            nextHoverRefresh = Time.unscaledTime + .1f;
        }

        var model = GetPlacedDeviceRoot(view.Device.InstanceId);
        if (view.OutlinedModel != model)
        {
            view.Outline?.Dispose();
            view.OutlinedModel = model;
            view.Outline = model != null ? new QuickOutline(model) : null;
            view.Outline?.SetColor(QuickOutline.HoverColor);
        }
        view.Outline?.SetVisible(true);
        device = hoveredDevice;
        return true;
    }

    internal void ClearPlacedDeviceHover()
    {
        hoveredView?.Outline?.SetVisible(false);
        hoveredView = null;
        hoveredDevice = default;
        nextHoverRefresh = 0f;
    }

    private bool TryPickPlaced(Vector2 pointer, out PlacedDeviceView picked)
    {
        picked = null;
        Ray ray = ViewCamera.ScreenPointToRay(pointer);
        int excluded = IsMovingPlacedDevice ? SelectedInstanceId : 0;
        if (Controller.TryPickPlacedDevice(ray, excluded, out int id) && placedObjects.TryGetValue(id, out picked))
            return true;

        // The footprint remains pickable when exhausted devices have disabled gameplay colliders.
        if (!TryGetBoardPoint(pointer, out var point)) return false;
        float nearest = float.PositiveInfinity;
        foreach (var view in placedObjects.Values)
        {
            var device = view.Device;
            if (device.InstanceId == excluded) continue;
            float distance = new Vector2(point.x - device.PlacementX, point.z - device.PlacementZ).sqrMagnitude;
            if (distance > device.Radius * device.Radius || distance >= nearest) continue;
            picked = view;
            nearest = distance;
        }
        return picked != null;
    }
}
