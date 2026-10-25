using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>A Build-only confirmation target. Hover and drag release never sell an item.</summary>
[DisallowMultipleComponent]
public sealed class DeviceSellAreaUI : MonoBehaviour, IPointerClickHandler
{
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
        if (!isActiveAndEnabled || !Placement || !Placement.CanEdit) return false;
        if (!canvas) canvas = GetComponentInParent<Canvas>();
        Camera camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, position, camera);
    }

    public void Refresh()
    {
        bool visible = Placement && Placement.CanEdit;
        bool selected = visible && Placement.TryGetSelectedDevice(out _);
        if (Group)
        {
            Group.alpha = visible ? 1 : 0;
            Group.blocksRaycasts = visible;
            Group.interactable = visible;
        }
        if (!visible) return;
        bool hover = ContainsPointer(Input.mousePosition);
        if (Background) Background.color = selected && hover
            ? new Color32(186, 44, 53, 248) : new Color32(104, 27, 35, 240);
        if (selected && Placement.TryGetSelectedDevice(out var device))
        {
            if (Title) Title.text = "售出「" + device.Name + "」  +" + device.SellPrice + " 金币";
            if (Hint) Hint.text = "左键点击此区域确认出售  ·  右键取消";
        }
        else
        {
            if (Title) Title.text = "装置售出区";
            if (Hint) Hint.text = "左键选中装置并移动  ·  再次左键放置  ·  R 旋转  ·  右键取消";
        }
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (!ContainsPointer(e.position)) return;
        if (e.button == PointerEventData.InputButton.Left) Placement.TrySellSelected();
        else if (e.button == PointerEventData.InputButton.Right) Placement.CancelPlacement();
        Refresh();
    }
    private void OnDisable()
    {
        if (Group) { Group.alpha = 0; Group.blocksRaycasts = Group.interactable = false; }
    }
}
