using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CashOutButtonUI : MonoBehaviour
{
    public MarbleGameController Controller;
    public Button CashOutButton;

    private void Reset()
    {
        CashOutButton = GetComponent<Button>();
    }

    private void Awake()
    {
        if (CashOutButton == null)
            CashOutButton = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (CashOutButton != null)
            CashOutButton.onClick.AddListener(OnCashOutClicked);
        RefreshInteractable();
    }

    private void OnDisable()
    {
        if (CashOutButton != null)
            CashOutButton.onClick.RemoveListener(OnCashOutClicked);
    }

    private void Update()
    {
        RefreshInteractable();
    }

    private void OnCashOutClicked()
    {
        if (Controller != null)
            Controller.CashOut();
        RefreshInteractable();
    }

    private void RefreshInteractable()
    {
        if (CashOutButton != null)
            CashOutButton.interactable = Controller != null && Controller.IsReady &&
                !Controller.Paused && Controller.Session.CanCashOut;
    }
}
