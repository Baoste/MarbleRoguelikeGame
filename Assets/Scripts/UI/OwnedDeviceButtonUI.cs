using MarblesECS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class OwnedDeviceButtonUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public TMP_Text NameText;
    public TMP_Text DescriptionText;
    public Button SelectButton;

    private int instanceId;
    private DevicePlacementUI placement;
    private bool dragged;

    private void Awake()
    {
        if (NameText == null)
            NameText = GetComponentInChildren<TMP_Text>(true);
        if (SelectButton == null)
            SelectButton = GetComponentInChildren<Button>(true);
    }

    private void OnDisable()
    {
        if (dragged && placement != null && placement.IsDraggingInventory) placement.CancelPlacement();
        dragged = false;
    }

    public void Bind(OwnedDeviceSnapshot device, bool canDrag, DevicePlacementUI placementUI)
    {
        placement = placementUI;
        instanceId = device.InstanceId;
        if (NameText != null)
            NameText.text = device.Name;
        if (DescriptionText != null)
            DescriptionText.text = MarblesECS.Presentation.DeviceDisplayText.Details(device) + "\n" + (device.Description ?? string.Empty);
        if (SelectButton != null)
            SelectButton.interactable = canDrag && !device.Placed;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && placement != null && SelectButton != null && SelectButton.IsInteractable())
            dragged = placement.BeginInventoryDrag(instanceId, e.position);
    }
    public void OnDrag(PointerEventData e) { if (dragged && placement != null) placement.UpdateInventoryDrag(e.position); }
    public void OnEndDrag(PointerEventData e)
    {
        if (!dragged || placement == null || e.button != PointerEventData.InputButton.Left) return;
        dragged = false;
        placement.EndInventoryDrag(e.position, e.pointerCurrentRaycast.gameObject);
    }
}
