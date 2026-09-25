using System;
using System.Collections.Generic;
using MarblesECS;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class DevicePlacementUI : MonoBehaviour
{
    [Serializable]
    public sealed class DevicePrefabBinding
    {
        [Min(1)] public int DefinitionId = 100;
        public GameObject Prefab;
    }

    public MarbleGameController Controller;
    public Camera ViewCamera;
    [Tooltip("装置位置以此对象的局部 X/Z 坐标保存。")]
    public Transform BoardRoot;
    [Tooltip("用于控制放置平面位置和旋转的 Transform。未绑定时使用 BoardRoot。")]
    public Transform PlacementPlane;
    public Vector2 PlacementPlaneSize = new Vector2(8f, 9.5f);
    public DevicePrefabBinding[] DevicePrefabs = Array.Empty<DevicePrefabBinding>();
    [FormerlySerializedAs("DeviceLocalY")]
    [Tooltip("装置沿 PlacementPlane 法线方向与平面的距离。")]
    public float DevicePlaneDistance;
    public bool SnapToGrid;
    [Tooltip("仅当 Canvas 没有全屏透明 Raycast 遮罩时开启。")]
    public bool BlockClicksOverUI;

    public int SelectedInstanceId { get; private set; }
    public string LastFeedback { get; private set; }

    private readonly Dictionary<int, GameObject> placedObjects = new Dictionary<int, GameObject>();
    private GameObject previewObject;
    private int displayedRevision = -1;
    private int displayedRoundId = -1;

    public void BeginPlacement(int instanceId)
    {
        if (!TryGetDevice(instanceId, out OwnedDeviceSnapshot device) || device.Placed)
        {
            LastFeedback = "Device can only be selected during Build.";
            return;
        }
        GameObject prefab = FindPrefab(device.DefinitionId);
        if (prefab == null && device.Kind == DeviceKind.LegacyPin)
        {
            Debug.LogError("No device prefab is bound for DefinitionId " + device.DefinitionId, this);
            LastFeedback = "Missing prefab for DefinitionId " + device.DefinitionId + ".";
            return;
        }

        CancelPlacement();
        SelectedInstanceId = instanceId;
        previewObject = prefab != null ? Instantiate(prefab, BoardRoot) : ContentDeviceView.Create(device, BoardRoot);
        previewObject.name = device.Name + " Preview";
        ConfigurePin(previewObject, device);
        SetPreviewPhysics(false);
        LastFeedback = "Move the pointer over the placement plane and left-click to place.";
    }

    public void CancelPlacement()
    {
        SelectedInstanceId = 0;
        if (previewObject != null)
            Destroy(previewObject);
        previewObject = null;
    }

    private void Update()
    {
        if (Controller == null || !Controller.IsReady || BoardRoot == null)
            return;

        SyncPlacedObjects();
        if (Controller.Session.Phase != RoundPhase.Build)
        {
            CancelPlacement();
            return;
        }
        if (SelectedInstanceId == 0 || previewObject == null)
            return;

        if (ViewCamera == null)
            ViewCamera = Camera.main;
        if (ViewCamera == null || !TryGetBoardPoint(out Vector3 localPoint))
        {
            previewObject.SetActive(false);
            return;
        }

        previewObject.SetActive(true);

        if (SnapToGrid && Controller.GridSize > 0f)
        {
            float grid = Controller.GridSize;
            localPoint.x = Mathf.Round(localPoint.x / grid) * grid;
            localPoint.z = Mathf.Round(localPoint.z / grid) * grid;
        }
        SetDeviceTransform(previewObject.transform, localPoint.x, localPoint.z);
        if (TryGetDevice(SelectedInstanceId, out var selected)) previewObject.transform.rotation *= Quaternion.Euler(0, selected.Angle, 0);

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.R)) Controller.RotateDevice(SelectedInstanceId, 45);
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            CancelPlacement();
            return;
        }
        bool pointerBlocked = BlockClicksOverUI && EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject();
        if (Input.GetMouseButtonDown(0) && !pointerBlocked)
        {
            Vector3 scale = previewObject.transform.lossyScale;
            if (IsInsidePlacementPlane(previewObject.transform.position) &&
                Controller.PlaceDevice(SelectedInstanceId, localPoint.x, localPoint.z,
                    previewObject.transform.position, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))))
            {
                LastFeedback = "Device placed.";
                CancelPlacement();
            }
            else
            {
                LastFeedback = "Placement rejected: outside the placement area or overlaps an obstacle/device.";
                Debug.LogWarning(LastFeedback, this);
            }
        }
#endif
    }

    private bool TryGetBoardPoint(out Vector3 localPoint)
    {
        GetPlacementPlanePose(out Vector3 planePosition, out Quaternion planeRotation);
        Vector3 planeUp = planeRotation * Vector3.up;
        Ray ray = ViewCamera.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(planeUp, planePosition);
        if (plane.Raycast(ray, out float distance))
        {
            Vector3 worldPoint = ray.GetPoint(distance);
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

        foreach (GameObject item in placedObjects.Values)
            if (item != null) { item.SetActive(false); Destroy(item); }
        placedObjects.Clear();

        foreach (OwnedDeviceSnapshot device in Controller.GetOwnedDevices())
        {
            if (!device.Placed) continue;
            GameObject prefab = FindPrefab(device.DefinitionId);
            if (prefab == null && device.Kind == DeviceKind.LegacyPin) continue;
            GameObject item = prefab != null ? Instantiate(prefab, BoardRoot) : ContentDeviceView.Create(device, BoardRoot);
            item.name = device.Name + " #" + device.InstanceId;
            SetDeviceTransform(item.transform, device.X, device.Z);
            item.transform.rotation *= Quaternion.Euler(0, device.Angle, 0);
            ConfigurePin(item, device);
            placedObjects.Add(device.InstanceId, item);
        }
        Physics.SyncTransforms();
    }

    private void ConfigurePin(GameObject item, OwnedDeviceSnapshot device)
    {
        if (device.Kind != DeviceKind.LegacyPin) { ContentDeviceView.Configure(item, device); return; }
        MarblePin pin = item.GetComponentInChildren<MarblePin>(true);
        if (pin == null)
            pin = item.AddComponent<MarblePin>();
        pin.TargetId = checked(100000 + device.InstanceId);
        pin.ScoreMultiplier = (float)device.ScoreMultiplier;
        pin.RushChanceAdd = (float)device.RushChanceAdd;
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

    private void SetPreviewPhysics(bool enabled)
    {
        foreach (Collider collider in previewObject.GetComponentsInChildren<Collider>(true))
            collider.enabled = enabled;
        foreach (MarblePin pin in previewObject.GetComponentsInChildren<MarblePin>(true))
            pin.enabled = enabled;
        foreach (MarbleDevice device in previewObject.GetComponentsInChildren<MarbleDevice>(true)) device.enabled = enabled;
    }

    private GameObject FindPrefab(uint definitionId)
    {
        foreach (DevicePrefabBinding binding in DevicePrefabs)
            if (binding != null && binding.DefinitionId > 0 && (uint)binding.DefinitionId == definitionId)
                return binding.Prefab;
        return null;
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
