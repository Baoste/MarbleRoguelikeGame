using System;
using MarblesECS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OwnedDeviceButtonUI : MonoBehaviour
{
    public TMP_Text NameText;
    public TMP_Text DescriptionText;
    public Button SelectButton;

    private int instanceId;
    private Action<int> selected;

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
        if (SelectButton != null)
            SelectButton.onClick.RemoveListener(HandleClicked);
    }

    public void Bind(OwnedDeviceSnapshot device, bool canSelect, Action<int> onSelected)
    {
        instanceId = device.InstanceId;
        selected = onSelected;
        if (NameText != null)
            NameText.text = device.Name;
        if (DescriptionText != null)
            DescriptionText.text = device.Description ?? string.Empty;
        if (SelectButton != null)
            SelectButton.interactable = canSelect && !device.Placed;
    }

    private void HandleClicked()
    {
        selected?.Invoke(instanceId);
    }
}
