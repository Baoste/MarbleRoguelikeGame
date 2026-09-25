using System.Collections.Generic;
using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Creates drug inventory entries from a UI prefab. Optional TMP child names:
/// NameText, CountText, DescriptionText, DurationText and ActiveText.
/// UseButton is preferred; otherwise the first Button and TMP text are used.
/// </summary>
[DisallowMultipleComponent]
public sealed class OwnedDrugPanelUI : MonoBehaviour
{
    public MarbleGameController Controller;
    public Transform Content;
    [Tooltip("药物条目 prefab，包含 Button 和 TMP 文本即可。可选子节点：NameText、CountText、DescriptionText、DurationText、ActiveText、UseButton。")]
    public GameObject DrugPrefab;
    [Tooltip("只显示有库存或效果仍在生效的药物。")]
    public bool OnlyShowOwned = true;
    [Tooltip("此面板启用时隐藏旧版 OnGUI 药物面板。")]
    public bool HideFallbackPanel = true;

    private readonly List<DrugItem> drugItems = new List<DrugItem>();
    private float nextRefreshTime;
    private ContentDrugPanel hiddenFallback;
    private bool fallbackWasEnabled;
    private RoundPhase displayedPhase = (RoundPhase)byte.MaxValue;
    private bool displayedPaused;

    private sealed class DrugItem
    {
        public GameObject Root;
        public TMP_Text Name, Count, Description, Duration, Active;
        public Button UseButton;
        public UnityAction Clicked;
        public uint DefinitionId;
    }

    private void OnEnable()
    {
        nextRefreshTime = 0;
        Refresh();
    }

    private void OnDisable()
    {
        foreach (DrugItem item in drugItems)
            if (item.UseButton != null) item.UseButton.interactable = false;
        RestoreFallback();
    }

    private void Update()
    {
        UpdateFallback();
        if (Controller == null || !Controller.IsReady)
        {
            HideItems(0);
            nextRefreshTime = 0;
            return;
        }

        if (Time.unscaledTime >= nextRefreshTime ||
            displayedPhase != Controller.Session.Phase || displayedPaused != Controller.Paused)
            Refresh();
    }

    public void Refresh()
    {
        if (Controller == null || !Controller.IsReady || Content == null || DrugPrefab == null)
        {
            HideItems(0);
            return;
        }

        displayedPhase = Controller.Session.Phase;
        displayedPaused = Controller.Paused;
        bool canUse = isActiveAndEnabled && !displayedPaused && displayedPhase == RoundPhase.Playing;
        int visibleCount = 0;

        foreach (DrugInventorySnapshot drug in Controller.GetDrugInventory())
        {
            if (OnlyShowOwned && drug.Count <= 0 && drug.ActiveDoses <= 0) continue;
            DrugItem item = GetOrCreateItem(visibleCount++);
            item.DefinitionId = drug.DefinitionId;
            string active = drug.ActiveDoses > 0
                ? "生效 " + drug.ActiveDoses + " 层 · 剩余 " + drug.SecondsRemaining.ToString("0.0") + " 秒"
                : string.Empty;
            string title = item.Count != null ? drug.Name : drug.Name + " ×" + drug.Count;
            if (item.Active == null && active.Length > 0) title += "\n" + active;
            SetText(item.Name, title);
            SetText(item.Count, "×" + drug.Count);
            SetText(item.Description, drug.Description);
            SetText(item.Duration, "持续 " + drug.DurationSeconds.ToString("0.#") + " 秒");
            SetText(item.Active, active);
            if (item.UseButton != null) item.UseButton.interactable = canUse && drug.Count > 0;
            item.Root.SetActive(true);
        }

        HideItems(visibleCount);
        nextRefreshTime = Time.unscaledTime + 0.1f;
    }

    private DrugItem GetOrCreateItem(int index)
    {
        while (drugItems.Count <= index)
        {
            GameObject instance = Instantiate(DrugPrefab, Content);
            // A copy of DeviceItem.prefab can also serve as the drug entry template.
            OwnedDeviceButtonUI deviceButton = instance.GetComponent<OwnedDeviceButtonUI>();
            if (deviceButton != null) deviceButton.enabled = false;
            TMP_Text[] texts = instance.GetComponentsInChildren<TMP_Text>(true);
            var item = new DrugItem
            {
                Root = instance,
                Name = FindText(texts, "NameText"),
                Count = FindText(texts, "CountText"),
                Description = FindText(texts, "DescriptionText"),
                Duration = FindText(texts, "DurationText"),
                Active = FindText(texts, "ActiveText")
            };
            // Do not reuse a dedicated detail field or a button's label as the title.
            if (item.Name == null)
                foreach (TMP_Text text in texts)
                    if (text != item.Count && text != item.Description && text != item.Duration &&
                        text != item.Active && text.GetComponentInParent<Button>() == null)
                    { item.Name = text; break; }
            if (item.Name == null && texts.Length == 1) item.Name = texts[0];

            Button[] buttons = instance.GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons)
                if (button.name == "UseButton") { item.UseButton = button; break; }
            if (item.UseButton == null && buttons.Length > 0) item.UseButton = buttons[0];
            if (item.UseButton != null)
            {
                item.Clicked = () => HandleUseClicked(item);
                item.UseButton.onClick.AddListener(item.Clicked);
            }
            if (item.Name == null || item.UseButton == null)
                Debug.LogWarning("药物条目 prefab 需要名称 TMP 文本和使用 Button。", instance);
            drugItems.Add(item);
        }
        return drugItems[index];
    }

    private void HandleUseClicked(DrugItem item)
    {
        if (!isActiveAndEnabled || Controller == null || !Controller.IsReady ||
            Controller.Paused || Controller.Session.Phase != RoundPhase.Playing) return;
        Controller.UseDrug(item.DefinitionId);
        Refresh();
    }

    private void HideItems(int start)
    {
        for (int i = start; i < drugItems.Count; i++)
            if (drugItems[i].Root != null) drugItems[i].Root.SetActive(false);
    }

    private static TMP_Text FindText(TMP_Text[] texts, string childName)
    {
        foreach (TMP_Text text in texts)
            if (text.name == childName) return text;
        return null;
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null && target.text != value) target.text = value;
    }

    private void UpdateFallback()
    {
        ContentDrugPanel fallback = HideFallbackPanel && Controller != null &&
            Content != null && DrugPrefab != null ? Controller.GetComponent<ContentDrugPanel>() : null;
        if (fallback == hiddenFallback) return;
        RestoreFallback();
        hiddenFallback = fallback;
        if (hiddenFallback == null) return;
        fallbackWasEnabled = hiddenFallback.enabled;
        hiddenFallback.enabled = false;
    }

    private void RestoreFallback()
    {
        if (hiddenFallback != null) hiddenFallback.enabled = fallbackWasEnabled;
        hiddenFallback = null;
    }

    private void OnDestroy()
    {
        RestoreFallback();
        foreach (DrugItem item in drugItems)
        {
            if (item.UseButton != null) item.UseButton.onClick.RemoveListener(item.Clicked);
            if (item.Root != null) Destroy(item.Root);
        }
    }
}
