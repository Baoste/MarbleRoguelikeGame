using MarblesECS.PhysX;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed partial class ShopUI : MonoBehaviour
{
    public MarbleGameController Controller;
    public CanvasGroup ShopCanvasGroup;
    public TMP_FontAsset TerminalFont;

    private void OnEnable() => RefreshView();
    private void Update() => RefreshView();

    private void RefreshView()
    {
        if (!ShopCanvasGroup) return;
        bool open = Controller && Controller.ShopOpen;
        ShopCanvasGroup.alpha = open ? 1f : 0f;
        ShopCanvasGroup.interactable = open && Controller.ShopCommandsAvailable;
        ShopCanvasGroup.blocksRaycasts = open;
        RefreshDailyView(open);
    }

    private void HandleCloseClicked()
    {
        Controller.TryCloseShop(Controller.Day.Version);
        RefreshView();
    }
}
