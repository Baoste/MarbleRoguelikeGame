using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MarblesECS;
using MarblesECS.PhysX;
using MarblesECS.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Read-only reference library. Its modal owns a temporary pause, never a new campaign.</summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed partial class ItemLibraryUI : MonoBehaviour
{
    public enum Category { All, Devices, Drugs }

    public MarbleGameController Controller;
    public DevicePlacementUI Placement;
    public TMP_FontAsset Font;
    public KeyCode ToggleKey = KeyCode.F1;
    [Tooltip("以 1920×1080 为基准；Canvas 会随 1080p—4K 等比例缩放。")]
    public Vector2 ReferenceResolution = new Vector2(1920, 1080);

    public bool IsOpen { get; private set; }
    public bool InputBlocked => IsOpen || releasePending;
    public int TotalEntryCount => entries.Count;
    public int DeviceEntryCount => entries.Count(e => e.Type == Category.Devices);
    public int DrugEntryCount => entries.Count(e => e.Type == Category.Drugs);
    public int VisibleEntryCount => filtered.Count;
    public Category SelectedCategory { get; private set; }
    public uint SelectedId => selected != null ? selected.Id : 0;
    public string SelectedDescription => selected != null ? selected.Description : string.Empty;
    public string SearchQuery { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public Canvas LibraryCanvas => libraryCanvas;

    private sealed class Entry
    {
        public uint Id;
        public Category Type;
        public string Name, Description, Facts, Guidance;
        public string SearchText;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<Entry> filtered = new List<Entry>();
    private readonly Dictionary<Behaviour, bool> suspended = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<UnityEngine.UI.Selectable, bool> blockedControls =
        new Dictionary<UnityEngine.UI.Selectable, bool>();
    private Entry selected;
    private bool wasPaused, ownsPause, releasePending;
    private int releaseFrame;

    private void Start()
    {
        ResolveReferences();
        BuildView();
        RefreshCatalog();
    }

    private void OnEnable()
    {
        if (libraryCanvas) libraryCanvas.gameObject.SetActive(true);
    }

    private void ResolveReferences()
    {
        if (!Controller) Controller = FindObjectsOfType<MarbleGameController>()
            .FirstOrDefault(c => c.gameObject.scene == gameObject.scene);
        if (!Placement) Placement = FindObjectsOfType<DevicePlacementUI>(true)
            .FirstOrDefault(p => p.Controller == Controller);
        if (!Font)
        {
            var rig = FindObjectsOfType<DiegeticMachineUI>(true)
                .FirstOrDefault(r => r.Controller == Controller && r.Font);
            if (rig) Font = rig.Font;
        }
        if (!Font) Font = TMP_Settings.defaultFontAsset;
    }

    private void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(ToggleKey)) Toggle();
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
#endif
        // Other presentation controls must not accidentally release this modal's pause.
        if (InputBlocked && Controller && !Controller.Paused) Controller.SetPaused(true);
        if (releasePending && Time.frameCount > releaseFrame + 1 && ControlsReleased())
            RestoreInteraction();
    }

    private void LateUpdate()
    {
        if (!InputBlocked) return;
        // Some existing HUD refreshers rewrite interactable every frame.
        foreach (var pair in blockedControls) if (pair.Key) pair.Key.interactable = false;
    }

    private static bool ControlsReleased()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return !Input.GetMouseButton(0) && !Input.GetMouseButton(1) &&
               !Input.GetKey(KeyCode.Space) && !Input.GetKey(KeyCode.Return) &&
               !Input.GetKey(KeyCode.KeypadEnter);
#else
        return true;
#endif
    }

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        if (!isActiveAndEnabled || IsOpen) return;
        ResolveReferences();
        BuildView();
        if (!Controller)
        {
            LastError = "道具库未绑定游戏控制器。";
            Debug.LogWarning(LastError, this);
            return;
        }
        if (!Controller.IsReady)
        {
            LastError = "游戏尚未初始化完成，请稍后打开道具库。";
            return;
        }
        if (!EventSystem.current)
        {
            LastError = "场景缺少启用的 EventSystem，请在场景中配置后再打开道具库。";
            Debug.LogWarning(LastError, this);
            return;
        }
        RefreshCatalog();
        if (!ownsPause)
        {
            wasPaused = Controller.Paused;
            ownsPause = true;
            Controller.SetPaused(true);
            SuspendInteraction();
        }
        releasePending = false;
        IsOpen = true;
        modal.gameObject.SetActive(true);
        openButton.gameObject.SetActive(false);
        var rig = FindObjectsOfType<DiegeticMachineUI>(true).FirstOrDefault(r => r.Controller == Controller);
        if (rig) rig.ClearHover();
        Canvas.ForceUpdateCanvases();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(search.gameObject);
        search.ActivateInputField();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ReleaseSearchFocus();
        if (modal) modal.gameObject.SetActive(false);
        if (openButton) openButton.gameObject.SetActive(true);
        // A close click or a Space typed into Search must not become gameplay input.
        releasePending = ownsPause;
        releaseFrame = Time.frameCount;
    }

    private void SuspendInteraction()
    {
        foreach (var control in FindObjectsOfType<UnityEngine.UI.Selectable>(true))
            if (control.gameObject.scene == Controller.gameObject.scene &&
                (!libraryCanvas || !control.transform.IsChildOf(libraryCanvas.transform)))
                blockedControls[control] = control.interactable;
        foreach (var p in FindObjectsOfType<DevicePlacementUI>(true))
            if (p.Controller == Controller) { p.CancelPlacement(); QueueSuspend(p); }
        foreach (var hud in FindObjectsOfType<MarbleGameHud>(true))
            if (hud.Controller == Controller) QueueSuspend(hud);
        foreach (var panel in FindObjectsOfType<ContentDrugPanel>(true))
            if (panel.Controller == Controller) QueueSuspend(panel);
        foreach (var activation in FindObjectsOfType<DeviceActivationUI>(true))
            if (activation.Controller == Controller) QueueSuspend(activation);
        foreach (var shop in FindObjectsOfType<ShopUI>(true))
            if (shop.Controller == Controller) QueueSuspend(shop);
        foreach (var panel in FindObjectsOfType<OwnedDevicePanelUI>(true))
            if (panel.Controller == Controller) QueueSuspend(panel);
        foreach (var panel in FindObjectsOfType<OwnedDrugPanelUI>(true))
            if (panel.Controller == Controller) QueueSuspend(panel);
        foreach (var item in FindObjectsOfType<ShopOfferItemUI>(true))
            if (item.gameObject.scene == Controller.gameObject.scene) QueueSuspend(item);
        foreach (var item in FindObjectsOfType<OwnedDeviceButtonUI>(true))
            if (item.gameObject.scene == Controller.gameObject.scene) QueueSuspend(item);
        // Legacy board views do not expose their Controller and process OnGUI outside uGUI.
        foreach (var board in FindObjectsOfType<MarbleBoardView>(true))
            if (board.gameObject.scene == Controller.gameObject.scene) QueueSuspend(board);
        // Snapshot everything before OnDisable callbacks can change another component's state.
        foreach (var pair in suspended) if (pair.Key) pair.Key.enabled = false;
        // OwnedDrugPanel.OnDisable may restore its legacy panel; keep that panel suspended too.
        foreach (var pair in suspended) if (pair.Key) pair.Key.enabled = false;
        foreach (var pair in blockedControls) if (pair.Key) pair.Key.interactable = false;
    }

    private void QueueSuspend(Behaviour component)
    {
        if (!component || suspended.ContainsKey(component)) return;
        suspended.Add(component, component.enabled);
    }

    private void RestoreInteraction()
    {
        releasePending = false;
        foreach (var pair in suspended) if (pair.Key) pair.Key.enabled = pair.Value;
        suspended.Clear();
        foreach (var pair in blockedControls) if (pair.Key) pair.Key.interactable = pair.Value;
        blockedControls.Clear();
        if (ownsPause && Controller) Controller.SetPaused(wasPaused);
        ownsPause = false;
        // Do not select an old gameplay button: keyboard submit after closing could activate it.
    }

    private void OnDisable()
    {
        IsOpen = false;
        ReleaseSearchFocus();
        if (modal) modal.gameObject.SetActive(false);
        if (openButton) openButton.gameObject.SetActive(true);
        if (libraryCanvas) libraryCanvas.gameObject.SetActive(false);
        RestoreInteraction();
    }

    private void OnDestroy() { RestoreInteraction(); }

    private void ReleaseSearchFocus()
    {
        if (search && search.textComponent) search.DeactivateInputField();
        var eventSystem = EventSystem.current;
        if (!eventSystem) return;
        var selectedObject = eventSystem.currentSelectedGameObject;
        if (selectedObject && libraryCanvas && selectedObject.transform.IsChildOf(libraryCanvas.transform))
            eventSystem.SetSelectedGameObject(null);
    }

    public void RefreshCatalog()
    {
        entries.Clear();
        LastError = string.Empty;
        try
        {
            var balance = Controller && Controller.Balance != null
                ? Controller.Balance : GameBalanceLoader.Load(Controller ? Controller.BalanceJson : null);
            // These definitions come from the same MRes catalog used by gameplay.
            foreach (var d in balance.Devices.OrderBy(d => d.Id))
            {
                string durability = d.Durability == -1 ? "无限" : d.Durability.ToString(CultureInfo.InvariantCulture);
                AddEntry(new Entry {
                    Id = d.Id, Type = Category.Devices, Name = d.Name, Description = d.Description,
                    Facts = "稀有度  " + DeviceDisplayText.Rarity(d.Rarity) +
                            "\n购买价格  " + d.Price + " 金币 / " + d.PurchaseQuantity + " 件" +
                            "\n单件售价  " + d.SellPrice + " 金币\n每回合耐久  " + durability,
                    Guidance = "在布置阶段将库存装置放到盘面。具体触发、主动操作及特殊条件以本条说明为准。" +
                               "\n耐久按有效作用消耗；耗尽后的当前效果会按装置规则完成。"
                });
            }
            foreach (var d in balance.Drugs.OrderBy(d => d.Id))
                AddEntry(new Entry {
                    Id = d.Id, Type = Category.Drugs, Name = d.Name, Description = d.Description,
                    Facts = "购买价格  " + d.Price + " 金币" +
                            "\n生效窗口  " + d.DurationSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " 秒" +
                            "\n重复使用  " + (d.Stackable ? "可叠加" : "不叠加"),
                    Guidance = "购买后显示在药剂库存中；游玩回合可使用。" +
                               "\n药剂在生效窗口内影响新发射的血珠，具体增益与代价见上方说明。"
                });
        }
        catch (Exception e)
        {
            entries.Clear();
            LastError = "道具资料读取失败：" + e.Message;
            Debug.LogError(LastError, this);
        }
        ApplyFilter();
    }

    private void AddEntry(Entry entry)
    {
        entry.Name = string.IsNullOrWhiteSpace(entry.Name) ? "未命名道具" : entry.Name;
        entry.Description = string.IsNullOrWhiteSpace(entry.Description) ? "暂无详细说明。" : entry.Description;
        entry.SearchText = entry.Name + "\n" + entry.Description + "\n" + entry.Id;
        entries.Add(entry);
    }

    public void SetCategory(Category category)
    {
        SelectedCategory = category;
        ApplyFilter();
    }

    public void SetSearch(string query)
    {
        SearchQuery = (query ?? string.Empty).Trim();
        if (search && search.text != (query ?? string.Empty)) search.SetTextWithoutNotify(query ?? string.Empty);
        ApplyFilter();
    }

    public bool SelectEntry(Category category, uint id)
    {
        var entry = filtered.FirstOrDefault(e => e.Type == category && e.Id == id);
        if (entry == null) return false;
        selected = entry;
        RefreshDetail();
        RefreshSelectionStyle();
        return true;
    }

    private void ApplyFilter()
    {
        var previous = selected;
        filtered.Clear();
        filtered.AddRange(entries.Where(e =>
            (SelectedCategory == Category.All || e.Type == SelectedCategory) &&
            (SearchQuery.Length == 0 || e.SearchText.IndexOf(SearchQuery, StringComparison.OrdinalIgnoreCase) >= 0)));
        selected = previous == null ? null : filtered.FirstOrDefault(e => e.Id == previous.Id && e.Type == previous.Type);
        if (selected == null) selected = filtered.FirstOrDefault();
        RefreshList();
        RefreshDetail();
    }
}
