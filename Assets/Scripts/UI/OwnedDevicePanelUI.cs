using System.Collections.Generic;
using MarblesECS;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OwnedDevicePanelUI : MonoBehaviour
{
    public MarbleGameController Controller;
    public Transform Content;
    public GameObject DevicePrefab;
    public DevicePlacementUI PlacementUI;
    public Button ContinueButton;
    [Tooltip("勾选后只显示尚未布置到桌面的装置。")]
    public bool OnlyShowUnplaced = true;

    private readonly List<OwnedDeviceButtonUI> deviceItems = new List<OwnedDeviceButtonUI>();
    private int displayedLayoutRevision = -1;
    private RoundPhase displayedPhase = (RoundPhase)byte.MaxValue;

    private void OnEnable()
    {
        if (ContinueButton != null)
            ContinueButton.onClick.AddListener(HandleContinueClicked);
        displayedLayoutRevision = -1;
        displayedPhase = (RoundPhase)byte.MaxValue;
        Refresh();
    }

    private void OnDisable()
    {
        if (ContinueButton != null)
            ContinueButton.onClick.RemoveListener(HandleContinueClicked);
    }

    private void Update()
    {
        if (Controller == null || !Controller.IsReady)
        {
            if (ContinueButton != null)
                ContinueButton.interactable = false;
            return;
        }

        if (ContinueButton != null)
            ContinueButton.interactable = Controller.Session.Phase == RoundPhase.Build;

        if (displayedLayoutRevision != Controller.Session.LayoutRevision ||
            displayedPhase != Controller.Session.Phase)
            Refresh();
    }

    public void Refresh()
    {
        if (Controller == null || !Controller.IsReady || Content == null || DevicePrefab == null)
            return;

        OwnedDeviceSnapshot[] devices = Controller.GetOwnedDevices();
        RoundPhase phase = Controller.Session.Phase;
        bool canSelect = phase == RoundPhase.Build;
        int visibleCount = 0;

        for (int i = 0; i < devices.Length; i++)
        {
            OwnedDeviceSnapshot device = devices[i];
            if (OnlyShowUnplaced && device.Placed)
                continue;

            OwnedDeviceButtonUI item = GetOrCreateItem(visibleCount++);
            item.Bind(device, canSelect, PlacementUI != null ? PlacementUI.BeginPlacement : null);
            item.gameObject.SetActive(true);
        }

        for (int i = visibleCount; i < deviceItems.Count; i++)
            deviceItems[i].gameObject.SetActive(false);

        displayedLayoutRevision = Controller.Session.LayoutRevision;
        displayedPhase = phase;
    }

    private OwnedDeviceButtonUI GetOrCreateItem(int index)
    {
        while (deviceItems.Count <= index)
        {
            GameObject instance = Instantiate(DevicePrefab, Content);
            OwnedDeviceButtonUI item = instance.GetComponent<OwnedDeviceButtonUI>();
            if (item == null)
                item = instance.AddComponent<OwnedDeviceButtonUI>();
            deviceItems.Add(item);
        }
        return deviceItems[index];
    }

    private void HandleContinueClicked()
    {
        if (PlacementUI != null)
            PlacementUI.CancelPlacement();
        if (Controller != null)
            Controller.BeginRound();
    }
}
