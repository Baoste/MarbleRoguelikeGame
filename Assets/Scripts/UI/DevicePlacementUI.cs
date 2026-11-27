using System.Collections.Generic;
using MarblesECS;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;

[DisallowMultipleComponent]
public sealed partial class DevicePlacementUI : MonoBehaviour
{
    public MarbleGameController Controller;
    public Camera ViewCamera;
    public Transform BoardRoot => Controller != null ? Controller.DeviceOrigin : null;
    public Transform PlacementPlane => Controller != null ? Controller.DevicePlane : null;
    public float DevicePlaneDistance => Controller != null ? Controller.DevicePlaneDistance : 0;
    public Vector2 PlacementPlaneSize = new Vector2(8f, 9.5f);
    public bool SnapToGrid;
    [Tooltip("仅当 Canvas 没有全屏透明 Raycast 遮罩时开启。")]
    public bool BlockClicksOverUI;
    public DeviceSellAreaUI SellArea;

    public int SelectedInstanceId { get; private set; }
    public uint SelectedDefinitionId { get; private set; }
    public string LastFeedback { get; private set; }

    private readonly Dictionary<int, PlacedDeviceView> placedObjects = new Dictionary<int, PlacedDeviceView>();
    private DeviceViewHandle preview;
    private QuickOutline previewOutline;
    private GameObject previewObject => preview?.Root;
    private int displayedRevision = -1;
    private int displayedRoundId = -1;

    public void CancelPlacement()
    {
        RestoreSelectedView();
        IsMovingPlacedDevice = false;
        SelectedInstanceId = 0;
        SelectedDefinitionId = 0;
        installByClick = false;
        preview?.Dispose();
        preview = null;
        previewOutline?.Dispose();
        previewOutline = null;
    }

    private void Update()
    {
        if (!CanEdit)
        {
            CancelPlacement();
            if (Controller != null && Controller.IsReady && BoardRoot != null) SyncPlacedObjects();
            return;
        }
        SyncPlacedObjects();
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            CancelPlacement();
            LastFeedback = "已取消，装置保留原状。";
            return;
        }
        Vector2 pointer = Input.mousePosition;
        if (Input.GetMouseButtonDown(0)) HandleBoardPointerDown(pointer);
        if (Input.GetKeyDown(KeyCode.R)) RotateSelected(45);
        if (Input.GetMouseButtonUp(0) && IsMovingPlacedDevice) HandleBoardPointerUp(pointer);
        UpdatePlacementPointer(IsDraggingInventory && !installByClick ? dragPointer : pointer);
#else
        ValidateSelection();
#endif
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelPlacement();
    }

    private bool TryGetBoardPoint(Vector2 screenPoint, out Vector3 localPoint)
    {
        if (TryProjectOntoPlacementPlane(screenPoint, out var worldPoint) && IsInsidePlacementPlane(worldPoint))
        {
            localPoint = BoardRoot.InverseTransformPoint(worldPoint);
            return true;
        }
        localPoint = default;
        return false;
    }

    private bool TryProjectOntoPlacementPlane(Vector2 screenPoint, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (!ViewCamera) return false;
        GetPlacementPlanePose(out var planePosition, out var planeRotation);
        Ray ray = ViewCamera.ScreenPointToRay(screenPoint);
        var plane = new Plane(planeRotation * Vector3.up, planePosition);
        if (!plane.Raycast(ray, out float distance)) return false;
        worldPoint = ray.GetPoint(distance);
        return SimulationContext.IsFinite(worldPoint);
    }

    private bool IsInsidePlacementPlane(Vector3 worldPoint)
    {
        if (Controller != null && Controller.PlacementSurface != null &&
            !Controller.PlacementSurface.ContainsWorldPoint(worldPoint, 0)) return false;
        GetPlacementPlanePose(out Vector3 position, out Quaternion rotation);
        Vector3 point = Quaternion.Inverse(rotation) * (worldPoint - position);
        return Mathf.Abs(point.x) <= PlacementPlaneSize.x * 0.5f &&
            Mathf.Abs(point.z) <= PlacementPlaneSize.y * 0.5f;
    }

    private void SyncPlacedObjects()
    {
        int revision = Controller.Session.LayoutRevision;
        int roundId = Controller.Snapshot.RoundId;
        if (revision == displayedRevision && roundId == displayedRoundId)
            return;
        bool placementEnabled = Controller.HasProductionFeature(ProductionFeature.DevicePlacement);
        if (roundId != displayedRoundId || !placementEnabled)
        {
            CancelPlacement();
            ClearPlacedObjects();
        }
        displayedRevision = revision;
        displayedRoundId = roundId;
        if (!placementEnabled) return;
        var removed = new HashSet<int>(placedObjects.Keys);
        foreach (OwnedDeviceSnapshot device in Controller.GetOwnedDevices())
        {
            if (!device.Placed) continue;
            if (placedObjects.TryGetValue(device.InstanceId, out var existing))
            {
                removed.Remove(device.InstanceId);
                existing.Device = device;
                continue;
            }
            placedObjects.Add(device.InstanceId, new PlacedDeviceView { Device = device });
        }
        foreach (int id in removed)
        {
            var view = placedObjects[id];
            if (view == hoveredView) ClearPlacedDeviceHover();
            view.Dispose();
            placedObjects.Remove(id);
        }
        HideSelectedView();
    }

    private void GetPlacementPlanePose(out Vector3 position, out Quaternion rotation)
    {
        Transform planeTransform = PlacementPlane;
        position = planeTransform.position;
        rotation = planeTransform.rotation;
    }

    private bool TryGetDevice(int instanceId, out OwnedDeviceSnapshot result)
    {
        if (Controller != null && Controller.IsReady)
            foreach (OwnedDeviceSnapshot device in Controller.GetOwnedDevices())
                if (device.InstanceId == instanceId)
                {
                    result = device;
                    return true;
                }
        result = default;
        return false;
    }

    private void OnDisable()
    {
        ClearPlacedDeviceHover();
        CancelPlacement();
    }

    private void ClearPlacedObjects()
    {
        ClearPlacedDeviceHover();
        foreach (var item in placedObjects.Values) item.Dispose();
        hiddenPlacedObject = null;
        placedObjects.Clear();
    }

    private void OnDestroy() => ClearPlacedObjects();

    private void OnDrawGizmos()
    {
        if (BoardRoot == null) return;
        GetPlacementPlanePose(out Vector3 planePosition, out Quaternion planeRotation);

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = Matrix4x4.TRS(planePosition, planeRotation, Vector3.one);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(PlacementPlaneSize.x, 0.01f, PlacementPlaneSize.y));
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(PlacementPlaneSize.x, 0.01f, PlacementPlaneSize.y));
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
