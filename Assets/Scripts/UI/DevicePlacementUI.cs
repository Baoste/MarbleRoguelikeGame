using System;
using System.Collections.Generic;
using MarblesECS;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed partial class DevicePlacementUI : MonoBehaviour
{
    public MarbleGameController Controller;
    public Camera ViewCamera;
    [Tooltip("装置位置以此对象的局部 X/Z 坐标保存。")]
    public Transform BoardRoot;
    [Tooltip("用于控制放置平面位置和旋转的 Transform。未绑定时使用 BoardRoot。")]
    public Transform PlacementPlane;
    public Vector2 PlacementPlaneSize = new Vector2(8f, 9.5f);
    [FormerlySerializedAs("DeviceLocalY")]
    [Tooltip("装置沿 PlacementPlane 法线方向与平面的距离。")]
    public float DevicePlaneDistance;
    public bool SnapToGrid;
    [Tooltip("仅当 Canvas 没有全屏透明 Raycast 遮罩时开启。")]
    public bool BlockClicksOverUI;
    public DeviceSellAreaUI SellArea;

    public int SelectedInstanceId { get; private set; }
    public string LastFeedback { get; private set; }

    private readonly Dictionary<int, MRes.MResLoadHandle> placedObjects = new Dictionary<int, MRes.MResLoadHandle>();
    private MRes.MResLoadHandle preview;
    private GameObject previewObject => preview?.Root;
    private int displayedRevision = -1;
    private int displayedRoundId = -1;

    public void BeginPlacement(int instanceId)
    {
        BeginSelection(instanceId);
    }

    public void CancelPlacement()
    {
        RestoreSelectedView();
        IsDraggingInventory = false;
        IsMovingPlacedDevice = false;
        SelectedInstanceId = 0;
        preview?.Dispose();
        preview = null;
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
        ValidateSelection();
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            CancelPlacement();
            LastFeedback = "已取消，装置保留原状。";
            return;
        }
        if (Input.GetKeyDown(KeyCode.R)) RotateSelected(45);
        Vector2 pointer = IsDraggingInventory ? dragPointer : (Vector2)Input.mousePosition;
        UpdatePlacementPointer(pointer);
        if (Input.GetMouseButtonDown(0) && !IsDraggingInventory) HandleBoardClick(pointer);
#endif
    }

    private bool TryGetBoardPoint(out Vector3 localPoint)
        => TryGetBoardPoint(IsDraggingInventory ? dragPointer : (Vector2)Input.mousePosition, out localPoint);

    private bool TryGetBoardPoint(Vector2 screenPoint, out Vector3 localPoint)
    {
        GetPlacementPlanePose(out Vector3 planePosition, out Quaternion planeRotation);
        Vector3 planeUp = planeRotation * Vector3.up;
        Ray ray = ViewCamera.ScreenPointToRay(screenPoint);
        var plane = new Plane(planeUp, planePosition);
        if (plane.Raycast(ray, out float distance))
        {
            Vector3 worldPoint = ray.GetPoint(distance);
            if (Controller != null && Controller.PlacementSurface != null &&
                !Controller.PlacementSurface.ContainsWorldPoint(worldPoint, 0))
            { localPoint = default; return false; }
            Vector3 planePoint = Quaternion.Inverse(planeRotation) * (worldPoint - planePosition);
            if (Mathf.Abs(planePoint.x) > PlacementPlaneSize.x * 0.5f ||
                Mathf.Abs(planePoint.z) > PlacementPlaneSize.y * 0.5f)
            {
                localPoint = default;
                return false;
            }
            localPoint = BoardRoot.InverseTransformPoint(worldPoint);
            return true;
        }
        localPoint = default;
        return false;
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
        displayedRevision = revision;
        displayedRoundId = roundId;

        foreach (var item in placedObjects.Values) item.Dispose();
        hiddenPlacedObject = null;
        placedObjects.Clear();
        foreach (OwnedDeviceSnapshot device in Controller.GetOwnedDevices())
        {
            if (!device.Placed) continue;
            var handle = MResDeviceView.Create(device, BoardRoot, Controller.DeviceScript(device));
            var item = handle.Root;
            SetDeviceTransform(item.transform, device.X, device.Z);
            item.transform.rotation *= Quaternion.Euler(0, device.Angle, 0);
            placedObjects.Add(device.InstanceId, handle);
            item.SetActive(true);
        }
        HideSelectedView();
        Physics.SyncTransforms();
    }

    private void SetDeviceTransform(Transform device, float boardX, float boardZ)
    {
        GetPlacementPlanePose(out Vector3 planePosition, out Quaternion planeRotation);
        Vector3 planeUp = planeRotation * Vector3.up;
        Vector3 boardPoint = BoardRoot.TransformPoint(new Vector3(boardX, 0f, boardZ));
        var plane = new Plane(planeUp, planePosition);
        Vector3 boardNormal = BoardRoot.up;
        float denominator = Vector3.Dot(plane.normal, boardNormal);
        Vector3 pointOnPlane;

        if (Mathf.Abs(denominator) > 0.0001f)
        {
            float distance = -plane.GetDistanceToPoint(boardPoint) / denominator;
            pointOnPlane = boardPoint + boardNormal * distance;
        }
        else
        {
            pointOnPlane = plane.ClosestPointOnPlane(boardPoint);
        }

        device.SetPositionAndRotation(
            pointOnPlane + planeUp * DevicePlaneDistance,
            planeRotation);
    }

    private void GetPlacementPlanePose(out Vector3 position, out Quaternion rotation)
    {
        Transform planeTransform = PlacementPlane != null ? PlacementPlane : BoardRoot;
        position = planeTransform.position;
        rotation = planeTransform.rotation;
    }

    private bool TryGetDevice(int instanceId, out OwnedDeviceSnapshot result)
    {
        if (Controller != null && Controller.IsReady && Controller.Session.Phase == RoundPhase.Build)
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
        CancelPlacement();
    }

    private void OnDestroy()
    {
        CancelPlacement();
        foreach (var item in placedObjects.Values) item.Dispose();
        placedObjects.Clear();
    }

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
