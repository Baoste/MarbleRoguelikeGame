using MarblesECS.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Displays the Build-phase sale drop target.</summary>
[DisallowMultipleComponent]
public sealed class DeviceSellAreaUI : MonoBehaviour, IPointerClickHandler
{
    internal const string DragHint = "按住左键拖动  ·  松开放置  ·  R 旋转  ·  右键取消";
    public DevicePlacementUI Placement;
    public CanvasGroup Group;
    public UnityEngine.UI.Image Background;
    public TMP_Text Title, Hint;
    private Canvas canvas;

    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        Refresh();
    }
    private void Update() => Refresh();

    public bool ContainsPointer(Vector2 position)
    {
        if (!isActiveAndEnabled || !Placement || !Placement.CanEdit ||
            !Placement.Controller.Session.Production.AllowDeviceSale ||
            Placement.Controller.Session.Phase != MarblesECS.RoundPhase.Build) return false;
        if (!canvas) canvas = GetComponentInParent<Canvas>();
        Camera camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, position, camera);
    }

    public void Refresh()
    {
        bool visible = Placement && Placement.CanEdit && Placement.Controller.Session.Production.AllowDeviceSale &&
            Placement.Controller.Session.Phase == MarblesECS.RoundPhase.Build;
        MarblesECS.OwnedDeviceSnapshot device = default;
        bool dragging = visible && Placement.TryGetSelectedDevice(out device);
        if (Group)
        {
            Group.alpha = visible ? 1 : 0;
            Group.blocksRaycasts = visible;
            Group.interactable = visible;
        }
        if (!visible) return;
        bool hover = ContainsPointer(Input.mousePosition);
        if (Background) Background.color = dragging && hover
            ? new Color32(186, 44, 53, 248) : new Color32(104, 27, 35, 240);
        if (dragging)
        {
            if (Title) Title.text = "售出" + DeviceDisplayText.SaleTarget(device) + "  +" + device.SellPrice + " 血肉残料";
            if (Hint) Hint.text = "拖到这里松开出售  ·  右键取消";
        }
        else
        {
            if (Title) Title.text = "装置售出区";
            if (Hint) Hint.text = DragHint;
        }
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right || !ContainsPointer(e.position)) return;
        Placement.CancelPlacement();
        Refresh();
    }
    private void OnDisable()
    {
        if (Group) { Group.alpha = 0; Group.blocksRaycasts = Group.interactable = false; }
    }
}
