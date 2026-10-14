using System;
using MarblesECS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class OwnedDeviceButtonUI : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public TMP_Text NameText;
    public TMP_Text DescriptionText;
    public Button SelectButton;

    private int instanceId;
    private Action<int> selected;
    private DevicePlacementUI placement;
    private bool dragged;
    private int ignoreClickFrame = -1;

    private void Awake()
    {
        if (NameText == null)
            NameText = GetComponentInChildren<TMP_Text>(true);
        if (SelectButton == null)
            SelectButton = GetComponentInChildren<Button>(true);
    }

    private void OnEnable()
    {
        if (SelectButton != null)
            SelectButton.onClick.AddListener(HandleClicked);
    }

    private void OnDisable()
    {
        if (dragged && placement != null && placement.IsDraggingInventory) placement.CancelPlacement();
        if (SelectButton != null)
            SelectButton.onClick.RemoveListener(HandleClicked);
    }

    public void Bind(OwnedDeviceSnapshot device, bool canSelect, Action<int> onSelected, DevicePlacementUI placementUI = null)
    {
        placement = placementUI;
        instanceId = device.InstanceId;
        selected = onSelected;
        if (NameText != null)
            NameText.text = device.Name;
        if (DescriptionText != null)
            DescriptionText.text = MarblesECS.Presentation.DeviceDisplayText.Details(device) + "\n" + (device.Description ?? string.Empty);
        if (SelectButton != null)
            SelectButton.interactable = canSelect && !device.Placed;
    }

    private void HandleClicked()
    {
        if (dragged || ignoreClickFrame == Time.frameCount) return;
        selected?.Invoke(instanceId);
    }
    public void OnPointerDown(PointerEventData e) { dragged = false; ignoreClickFrame = -1; }
    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && placement != null && SelectButton != null && SelectButton.IsInteractable())
            dragged = placement.BeginInventoryDrag(instanceId, e.position);
    }
    public void OnDrag(PointerEventData e) { if (dragged && placement != null) placement.UpdateInventoryDrag(e.position); }
    public void OnEndDrag(PointerEventData e)
    {
        if (!dragged || placement == null) return;
        dragged = false; ignoreClickFrame = Time.frameCount;
        placement.EndInventoryDrag(e.position, e.pointerCurrentRaycast.gameObject);
    }
}
