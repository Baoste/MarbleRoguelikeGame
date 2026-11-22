using System;
using MarblesECS;
using MarblesECS.Presentation;
using UnityEngine;

public sealed partial class DevicePlacementUI
{
    private sealed class PlacedDeviceView : IDisposable
    {
        private readonly MRes.MResLoadHandle handle;
        internal readonly OwnedDeviceSnapshot Device;
        internal readonly Collider[] Colliders;
        internal QuickOutline Outline;
        internal GameObject Root => handle.Root;
        internal bool IsVisible => Root && Root.activeInHierarchy;

        internal PlacedDeviceView(MRes.MResLoadHandle handle, OwnedDeviceSnapshot device)
        {
            this.handle = handle;
            Device = device;
            Colliders = Root.GetComponentsInChildren<Collider>(true);
        }

        public void Dispose()
        {
            Outline?.Dispose();
            handle.Dispose();
        }
    }

    private PlacedDeviceView hoveredView;
    private OwnedDeviceSnapshot hoveredDevice;
    private float nextHoverRefresh;

    internal GameObject GetPlacedDeviceRoot(int instanceId) =>
        placedObjects.TryGetValue(instanceId, out var view) ? view.Root : null;

    internal bool UpdatePlacedDeviceHover(Vector2 pointer, out OwnedDeviceSnapshot device)
    {
        device = default;
        if (!isActiveAndEnabled || !Controller || !Controller.IsReady || !BoardRoot ||
            SelectedInstanceId != 0 || placedObjects.Count == 0)
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

        if (view.Outline == null)
        {
            view.Outline = new QuickOutline(view.Root);
            view.Outline.SetColor(QuickOutline.HoverColor);
        }
        view.Outline.SetVisible(true);
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
        float nearest = float.PositiveInfinity;
        foreach (var view in placedObjects.Values)
        {
            if (!view.IsVisible) continue;
            foreach (var collider in view.Colliders)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                    !collider.Raycast(ray, out var hit, nearest)) continue;
                picked = view;
                nearest = hit.distance;
            }
        }
        if (picked != null) return true;

        // The footprint remains pickable when exhausted devices have disabled gameplay colliders.
        if (!TryGetBoardPoint(pointer, out var point)) return false;
        foreach (var view in placedObjects.Values)
        {
            if (!view.IsVisible) continue;
            var device = view.Device;
            float distance = new Vector2(point.x - device.PlacementX, point.z - device.PlacementZ).sqrMagnitude;
            if (distance > device.Radius * device.Radius || distance >= nearest) continue;
            picked = view;
            nearest = distance;
        }
        return picked != null;
    }
}
