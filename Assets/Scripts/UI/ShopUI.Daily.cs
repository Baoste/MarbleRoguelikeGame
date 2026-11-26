using System;
using System.Collections.Generic;
using MarblesECS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ShopUI
{
    private static readonly Color TerminalInk = new Color(.91f, .88f, .79f);
    private static readonly Color TerminalMuted = new Color(.64f, .69f, .66f);
    private static readonly Color TicketColor = new Color(.94f, .77f, .45f);
    private static readonly Color TerminalPanel = new Color(.095f, .12f, .12f);
    private static readonly Color TerminalButton = new Color(.16f, .21f, .21f);
    private static readonly Color TerminalSelected = new Color(.18f, .31f, .26f);
    private RectTransform dailyRoot;
    private DailyCards backpackCards, availableCards, shopCards;
    private TMP_Text dayTitle, ticketBalance, backpackTitle, shopTitle, dailyFeedback, refreshLabel;
    private Button confirmLoadout, refreshStock;
    private (long dayVersion, int loadoutRevision, int refresh, bool commands) lastDailyView;
    private bool dailyWasOpen;

    private void RefreshDailyView(bool open)
    {
        if (!open)
        {
            dailyWasOpen = false;
            return;
        }
        if (!ShopCanvasGroup) return;
        if (!dailyRoot) BuildDailyView();
        var day = Controller.Day;
        var loadout = Controller.DailyLoadout;
        var session = Controller.Session;
        var current = (day.Version, loadout.Revision, session.ShopRefreshCount, Controller.ShopCommandsAvailable);
        if (!dailyWasOpen || !lastDailyView.Equals(current))
        {
            if (!dailyWasOpen) dailyFeedback.text = string.Empty;
            lastDailyView = current;
            dailyWasOpen = true;
            dayTitle.text = $"DAY {day.DayIndex}   装置选择";
            ticketBalance.text = $"公司工票  {session.CompanyScripBalance}\n用于抽取装置使用权限";
            backpackTitle.text = $"今日背包  {loadout.SelectedCount} / {loadout.Capacity}";
            confirmLoadout.interactable = loadout.CanConfirm;
            var config = Controller.CurrentDayDefinition.DailyLoadout.Shop;
            RefreshDailyCards(day.Version, loadout, session.CompanyScripBalance, config);
        }
        backpackCards.Resize();
        availableCards.Resize();
        shopCards.Resize();
    }

    private void RefreshDailyCards(long version, DailyLoadoutSnapshot loadout, long scrip, DailyShopDefinition shop)
    {
        int backpack = 0;
        int available = 0;
        foreach (var peg in Controller.GetDailyPegs())
        {
            string durability = peg.Durability < 0 ? "无限耐久" : $"耐久 {peg.Durability}";
            string detail = $"安装 {peg.InstallPrice} 血肉残料 · {durability}\n{peg.Description}";
            if (peg.Selected)
                backpackCards.Bind(backpack++, peg.DefinitionId, peg.Name, detail, peg.Required ? "必选" : "点击移除",
                    true, peg.Required ? (Action)null : () => ToggleDailyPeg(version, peg.DefinitionId));
            if (peg.Permitted)
            {
                bool canSelect = !peg.Required && (peg.Selected || loadout.SelectedCount < loadout.Capacity);
                availableCards.Bind(available++, peg.DefinitionId, peg.Name, detail,
                    peg.Required ? "必选" : peg.Selected ? "背包中" : canSelect ? "加入背包" : "背包已满",
                    peg.Selected, canSelect ? (Action)(() => ToggleDailyPeg(version, peg.DefinitionId)) : null);
            }
        }
        backpackCards.Trim(backpack);
        availableCards.Trim(available);

        int count = 0;
        if (shop != null)
            foreach (var offer in Controller.GetShopOffers())
            {
                bool canDraw = !offer.Sold && loadout.Draws < shop.SelectionLimit && scrip >= offer.Price;
                bool canSelect = offer.Sold && (offer.Selected || loadout.SelectedCount < loadout.Capacity);
                string state = offer.Sold ? offer.Selected ? "已抽取 · 背包中" : "已抽取 · 可选"
                    : $"{offer.Price} 张工票 · {(canDraw ? "抽取" : scrip < offer.Price ? "工票不足" : "抽取已达上限")}";
                Action request = canDraw ? (Action)(() => DrawPermission(offer)) :
                    canSelect ? (Action)(() => ToggleDailyPeg(version, offer.DefinitionId)) : null;
                shopCards.Bind(count++, offer.DefinitionId, offer.Name, offer.Description, state, offer.Sold, request);
            }
        shopCards.Trim(count);
        shopTitle.text = shop == null ? "商店抽取 · 暂时关闭" : count == 0 ? "商店抽取 · 当前无可抽取权限"
            : $"商店抽取  {loadout.Draws} / {shop.SelectionLimit}";
        refreshStock.gameObject.SetActive(shop?.RefreshCost != null);
        refreshStock.interactable = Controller.CanRefreshDailyShop;
        refreshLabel.text = shop?.RefreshCost == null ? string.Empty : $"刷新 · {shop.RefreshCost.Value} 工票";
    }

    private void ToggleDailyPeg(long version, uint id)
    {
        if (Controller.TryToggleLoadoutPeg(version, id)) dailyFeedback.text = string.Empty;
        RefreshDailyView(Controller.ShopOpen);
    }

    private void DrawPermission(ShopOfferSnapshot offer)
    {
        dailyFeedback.text = Controller.BuyOffer(offer.Index, offer.DefinitionId)
            ? $"获得「{offer.Name}」使用权限。可在上方加入背包。"
            : "抽取未完成，请检查工票与剩余次数。";
        RefreshDailyView(Controller.ShopOpen);
    }

    private void BuildDailyView()
    {
        var group = ShopCanvasGroup.transform;
        for (int i = 0; i < group.childCount; i++) group.GetChild(i).gameObject.SetActive(false);
        dailyRoot = Panel("Daily Loadout", group, new Rect(.055f, .055f, .89f, .89f), new Color(.065f, .086f, .086f));
        dayTitle = Label(dailyRoot, "日期", new Rect(.027f, .883f, .59f, .09f), 44, TerminalInk);
        var ticket = Panel("工票图标", dailyRoot, new Rect(.653f, .924f, .025f, .026f), TicketColor);
        Panel("票面", ticket, new Rect(.18f, .24f, .64f, .52f), TerminalPanel);
        Panel("票面戳记", ticket, new Rect(.64f, .26f, .07f, .48f), TicketColor);
        ticketBalance = Label(dailyRoot, "公司工票", new Rect(.692f, .886f, .28f, .085f), 26, TicketColor);
        var backpack = Panel("今日背包", dailyRoot, new Rect(.023f, .107f, .295f, .754f), TerminalPanel);
        var options = Panel("可选装置", dailyRoot, new Rect(.334f, .49f, .642f, .371f), TerminalPanel);
        var shop = Panel("商店抽取", dailyRoot, new Rect(.334f, .107f, .642f, .368f), TerminalPanel);
        backpackTitle = Label(backpack, "容量", new Rect(.055f, .899f, .89f, .085f), 32, TerminalInk);
        Label(options, "标题", new Rect(.025f, .83f, .94f, .14f), 30, TerminalInk).text = "可选装置";
        shopTitle = Label(shop, "标题", new Rect(.025f, .83f, .66f, .14f), 30, TerminalInk);
        backpackCards = new DailyCards(this, backpack, new Rect(.05f, .03f, .9f, .862f), 1);
        availableCards = new DailyCards(this, options, new Rect(.024f, .045f, .952f, .772f), 2);
        shopCards = new DailyCards(this, shop, new Rect(.024f, .045f, .952f, .772f), 2);
        refreshStock = DailyButton(shop, "刷新", new Rect(.746f, .835f, .23f, .128f), "", () =>
        {
            if (Controller.RefreshShop()) dailyFeedback.text = "商品已刷新。";
            RefreshDailyView(Controller.ShopOpen);
        }, out refreshLabel);
        dailyFeedback = Label(dailyRoot, "反馈", new Rect(.025f, .022f, .64f, .057f), 23, TicketColor);
        DailyButton(dailyRoot, "返回房间", new Rect(.671f, .022f, .128f, .056f), "返回房间", HandleCloseClicked, out _);
        confirmLoadout = DailyButton(dailyRoot, "确认背包", new Rect(.811f, .022f, .164f, .056f), "确认背包", () =>
        {
            long version = Controller.Day.Version;
            if (Controller.TryConfirmLoadout(version)) HandleCloseClicked();
        }, out _);
        confirmLoadout.targetGraphic.color = TerminalSelected;
    }

    private static RectTransform Area(string name, Transform parent, Rect anchors)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchors.min;
        rect.anchorMax = anchors.max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static RectTransform Panel(string name, Transform parent, Rect anchors, Color color)
    {
        var rect = Area(name, parent, anchors);
        rect.gameObject.AddComponent<Image>().color = color;
        return rect;
    }

    private TMP_Text Label(Transform parent, string name, Rect anchors, float size, Color color)
    {
        var rect = Area(name, parent, anchors);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (CoinsText) label.font = CoinsText.font;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = size * .65f;
        label.fontSizeMax = size;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private Button DailyButton(Transform parent, string name, Rect anchors, string text, Action click, out TMP_Text label)
    {
        var rect = Panel(name, parent, anchors, TerminalButton);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        button.onClick.AddListener(() => click());
        label = Label(rect, "文字", new Rect(.05f, .05f, .9f, .9f), 24, TerminalInk);
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private sealed class DailyCards
    {
        private readonly ShopUI owner;
        private readonly RectTransform viewport, content;
        private readonly GridLayoutGroup grid;
        private readonly List<Card> cards = new List<Card>();
        private readonly int columns;

        internal DailyCards(ShopUI owner, Transform parent, Rect area, int columns)
        {
            this.owner = owner;
            this.columns = columns;
            var scroll = Area("装置列表", parent, area).gameObject.AddComponent<ScrollRect>();
            viewport = Panel("视窗", scroll.transform, new Rect(0, 0, 1, 1), Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Area("卡片", viewport, new Rect(0, 1, 1, 0));
            content.pivot = new Vector2(.5f, 1);
            grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.spacing = new Vector2(9, 9);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 28;
            scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        internal void Resize()
        {
            float width = Mathf.Max(1, (viewport.rect.width - (columns - 1) * grid.spacing.x) / columns);
            float height = Mathf.Clamp(viewport.rect.height * .42f, 92, 136);
            var desired = new Vector2(width, height);
            if (grid.cellSize != desired) grid.cellSize = desired;
        }

        internal void Bind(int index, uint id, string title, string description, string state, bool selected, Action action)
        {
            while (cards.Count <= index) cards.Add(new Card(owner, content));
            var card = cards[index];
            card.Button.gameObject.SetActive(true);
            card.Click = action;
            card.Button.interactable = action != null;
            card.Button.targetGraphic.color = selected ? TerminalSelected : TerminalButton;
            card.Title.text = title;
            card.Description.text = description;
            card.State.text = state;
            var icon = DeviceResourceCatalog.Current.Get(id).Icon;
            card.Icon.texture = icon;
            card.Icon.enabled = icon;
            card.Fallback.enabled = !icon;
        }

        internal void Trim(int length)
        {
            for (int i = length; i < cards.Count; i++) cards[i].Button.gameObject.SetActive(false);
        }

        private sealed class Card
        {
            internal readonly Button Button;
            internal readonly TMP_Text Title, Description, State, Fallback;
            internal readonly RawImage Icon;
            internal Action Click;

            internal Card(ShopUI owner, Transform parent)
            {
                var panel = Panel("装置", parent, new Rect(), TerminalButton);
                Button = panel.gameObject.AddComponent<Button>();
                Button.targetGraphic = panel.GetComponent<Image>();
                Button.onClick.AddListener(() => Click?.Invoke());
                var iconArea = Area("图标区域", panel, new Rect(.026f, .51f, .114f, .39f));
                Icon = Area("图标", iconArea, new Rect(0, 0, 1, 1)).gameObject.AddComponent<RawImage>();
                Icon.raycastTarget = false;
                Icon.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                Fallback = owner.Label(panel, "装置标识", new Rect(.044f, .49f, .1f, .38f), 28, TicketColor);
                Fallback.text = "◇";
                Title = owner.Label(panel, "名称", new Rect(.156f, .67f, .81f, .26f), 26, TerminalInk);
                Description = owner.Label(panel, "说明", new Rect(.156f, .253f, .81f, .41f), 21, TerminalMuted);
                State = owner.Label(panel, "状态", new Rect(.156f, .045f, .81f, .18f), 20, TicketColor);
            }
        }
    }
}
