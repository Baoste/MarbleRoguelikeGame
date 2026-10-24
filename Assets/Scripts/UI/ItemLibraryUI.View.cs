using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public sealed partial class ItemLibraryUI
{
    private static readonly Color Ink = new Color(.075f, .065f, .059f, 1);
    private static readonly Color Panel = new Color(.15f, .13f, .11f, 1);
    private static readonly Color Raised = new Color(.23f, .20f, .16f, 1);
    private static readonly Color Paper = new Color(.91f, .86f, .73f, 1);
    private static readonly Color Muted = new Color(.66f, .63f, .53f, 1);
    private static readonly Color Blood = new Color(.46f, .11f, .09f, 1);
    private Canvas libraryCanvas;
    private RectTransform modal, listContent, detailContent;
    private UnityEngine.UI.Button openButton;
    private TMP_InputField search;
    private TMP_Text countLabel, detailTitle, detailType, detailFacts, detailDescription, detailGuidance;
    private UnityEngine.UI.ScrollRect listScroll, detailScroll;
    private readonly List<Row> rows = new List<Row>();
    private readonly List<UnityEngine.UI.Button> categoryButtons = new List<UnityEngine.UI.Button>();

    private sealed class Row
    {
        public Entry Data;
        public UnityEngine.UI.Button Button;
        public TMP_Text Name, Subtitle;
    }

    private void BuildView()
    {
        if (libraryCanvas) return;
        try { BuildViewContents(); }
        catch
        {
            // A failed TMP setup must not leave a full-screen raycast shield in the scene.
            if (libraryCanvas)
            {
                libraryCanvas.gameObject.SetActive(false);
                Destroy(libraryCanvas.gameObject);
            }
            libraryCanvas = null;
            modal = listContent = detailContent = null;
            openButton = null;
            search = null;
            countLabel = detailTitle = detailType = detailFacts = detailDescription = detailGuidance = null;
            listScroll = detailScroll = null;
            rows.Clear();
            categoryButtons.Clear();
            throw;
        }
    }

    private void BuildViewContents()
    {
        var canvasObject = new GameObject("道具库界面", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        // Keep all Selectables inactive until their references and child text are wired.
        canvasObject.SetActive(false);
        canvasObject.layer = 5;
        canvasObject.transform.SetParent(transform, false);
        libraryCanvas = canvasObject.GetComponent<Canvas>();
        libraryCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        libraryCanvas.sortingOrder = 32760;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;
        openButton = MakeButton(canvasObject.transform, "打开道具库", "道具库  /  " + ToggleKey, Open);
        Anchor(openButton.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-260, -108), new Vector2(-28, -54));

        modal = Rect("道具库_全屏", canvasObject.transform);
        Stretch(modal);
        // A real selectable intercepts controls that intentionally ignore decorative Images.
        var shield = MakeButton(modal, "全屏输入遮罩", string.Empty, () => { });
        Stretch(shield.GetComponent<RectTransform>());
        shield.GetComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, .82f);
        shield.transition = UnityEngine.UI.Selectable.Transition.None;

        var window = Rect("资料窗口", modal);
        Anchor(window, new Vector2(.04f, .055f), new Vector2(.96f, .945f), Vector2.zero, Vector2.zero);
        Background(window, Ink, true);
        var top = Rect("标题栏", window);
        Anchor(top, new Vector2(0, 1), Vector2.one, new Vector2(0, -104), Vector2.zero);
        Background(top, Panel);
        var title = Text(top, "道具库标题", "道具档案", 38, Paper);
        Anchor(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(32, 37), new Vector2(-460, -18));
        var subtitle = Text(top, "副标题", "DEVICE ARCHIVE   /   装置资料", 18, Muted);
        Anchor(subtitle.rectTransform, Vector2.zero, Vector2.one, new Vector2(34, 12), new Vector2(-440, -66));
        var close = MakeButton(top, "关闭", "关闭  /  Esc", Close);
        Anchor(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
            new Vector2(-230, -76), new Vector2(-28, -24));

        search = MakeSearch(window);
        Anchor(search.GetComponent<RectTransform>(), new Vector2(0, 1), Vector2.one,
            new Vector2(28, -184), new Vector2(-28, -128));

        var tabs = Rect("分类", window);
        Anchor(tabs, new Vector2(0, 1), new Vector2(.40f, 1), new Vector2(28, -252), new Vector2(-12, -204));
        var layout = tabs.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        foreach (Category type in Enum.GetValues(typeof(Category)))
        {
            var captured = type;
            var button = MakeButton(tabs, "类别_" + type, CategoryName(type), () => SetCategory(captured));
            button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
            categoryButtons.Add(button);
        }
        countLabel = Text(window, "结果数量", string.Empty, 21, Muted);
        Anchor(countLabel.rectTransform, new Vector2(.42f, 1), Vector2.one,
            new Vector2(0, -250), new Vector2(-32, -204));
        countLabel.alignment = TextAlignmentOptions.MidlineRight;

        listScroll = MakeScroll(window, "条目列表", out listContent);
        Anchor(listScroll.GetComponent<RectTransform>(), Vector2.zero, new Vector2(.40f, 1),
            new Vector2(28, 74), new Vector2(-12, -274));
        detailScroll = MakeScroll(window, "详细资料", out detailContent);
        Anchor(detailScroll.GetComponent<RectTransform>(), new Vector2(.42f, 0), Vector2.one,
            new Vector2(0, 74), new Vector2(-28, -274));
        detailScroll.GetComponent<UnityEngine.UI.Image>().color = Panel;
        var detailLayout = detailContent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        detailLayout.padding = new RectOffset(30, 30, 28, 28);
        detailLayout.spacing = 20;
        detailType = DetailText("类别标签", 20, Muted);
        detailTitle = DetailText("道具名称", 40, Paper);
        detailFacts = DetailText("参数", 25, Paper);
        detailDescription = DetailText("完整说明", 28, Paper);
        detailGuidance = DetailText("使用方式", 22, Muted);
        var footer = Text(window, "底栏", "浏览期间已暂停游戏   ·   这里只查看资料，不购买或使用道具   ·   鼠标滚轮查看完整内容", 20, Muted);
        Anchor(footer.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(30, 20), new Vector2(-28, 56));
        modal.gameObject.SetActive(false);
        canvasObject.SetActive(true);
    }

    private TMP_Text DetailText(string name, int size, Color color)
    {
        var text = Text(detailContent, name, string.Empty, size, color);
        text.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
        return text;
    }

    private TMP_InputField MakeSearch(Transform parent)
    {
        var root = Rect("搜索", parent);
        var image = Background(root, Raised, true);
        var input = root.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = image;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 100;
        input.contentType = TMP_InputField.ContentType.Standard;
        var viewport = Rect("TextArea", root);
        Stretch(viewport, 18, 8);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var label = Text(viewport, "输入文字", string.Empty, 26, Paper);
        Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        var placeholder = Text(viewport, "提示", "搜索名称、效果或编号…", 25, Muted);
        Stretch(placeholder.rectTransform);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        input.textViewport = viewport;
        input.textComponent = (TextMeshProUGUI)label;
        input.placeholder = placeholder;
        // TMP 3.0.7 fontAsset/pointSize setters dereference textComponent immediately.
        input.fontAsset = Font;
        input.pointSize = 26;
        input.richText = false;
        input.onValueChanged.AddListener(SetSearch);
        return input;
    }

    private UnityEngine.UI.ScrollRect MakeScroll(Transform parent, string name, out RectTransform content)
    {
        var root = Rect(name, parent);
        Background(root, Panel, true);
        var scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45;
        var viewport = Rect("Viewport", root);
        Anchor(viewport, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-14, 0));
        Background(viewport, Color.white);
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 8;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        var bar = Rect("Scrollbar", root);
        Anchor(bar, new Vector2(1, 0), Vector2.one, new Vector2(-10, 6), new Vector2(-3, -6));
        Background(bar, Ink);
        var handle = Rect("Handle", bar);
        Stretch(handle);
        Background(handle, Muted, true);
        var scrollbar = bar.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        return scroll;
    }

    private void RefreshList()
    {
        if (!listContent) return;
        while (rows.Count < filtered.Count)
        {
            var row = new Row();
            row.Button = MakeButton(listContent, "道具条目_" + rows.Count, string.Empty, () => {
                if (row.Data != null) SelectEntry(row.Data.Type, row.Data.Id);
            });
            row.Button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 112;
            var rect = row.Button.GetComponent<RectTransform>();
            row.Name = Text(rect, "名称", string.Empty, 28, Paper);
            Anchor(row.Name.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 43), new Vector2(-18, -14));
            row.Name.enableWordWrapping = false;
            row.Name.overflowMode = TextOverflowModes.Ellipsis;
            row.Subtitle = Text(rect, "类型", string.Empty, 20, Muted);
            Anchor(row.Subtitle.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 13), new Vector2(-18, -70));
            rows.Add(row);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            bool visible = i < filtered.Count;
            rows[i].Button.gameObject.SetActive(visible);
            if (!visible) { rows[i].Data = null; continue; }
            rows[i].Data = filtered[i];
            rows[i].Name.text = filtered[i].Name;
            rows[i].Subtitle.text = CategoryName(filtered[i].Type) + "   /   " + filtered[i].Id.ToString("000");
        }
        countLabel.text = LastError.Length > 0 ? "资料读取失败" :
            filtered.Count == 0 ? "没有匹配结果，请更换关键词" : "找到 " + filtered.Count + " 项  /  共 " + entries.Count + " 项";
        for (int i = 0; i < categoryButtons.Count; i++)
        {
            int count = i == 0 ? TotalEntryCount : DeviceEntryCount;
            categoryButtons[i].GetComponentInChildren<TMP_Text>().text = CategoryName((Category)i) + " " + count;
            SetButtonColor(categoryButtons[i], (int)SelectedCategory == i ? Blood : Raised);
        }
        listScroll.verticalNormalizedPosition = 1;
        RefreshSelectionStyle();
    }

    private void RefreshSelectionStyle()
    {
        foreach (var row in rows) SetButtonColor(row.Button, row.Data == selected && selected != null ? Blood : Raised);
    }

    private void RefreshDetail()
    {
        if (!detailTitle) return;
        detailType.text = selected == null ? "资料检索" : CategoryName(selected.Type) + "   /   编号 " + selected.Id;
        detailTitle.text = selected == null ? (LastError.Length > 0 ? "无法读取道具资料" : "没有匹配道具") : selected.Name;
        detailFacts.text = selected == null ? string.Empty : selected.Facts;
        detailDescription.text = selected == null ? (LastError.Length > 0 ? LastError : "尝试减少关键词，或切换到「全部」。") : "效果说明\n\n" + selected.Description;
        detailGuidance.text = selected == null ? string.Empty : "使用提示\n\n" + selected.Guidance;
        detailScroll.verticalNormalizedPosition = 1;
    }

    private static string CategoryName(Category type) => type == Category.Devices ? "装置" : "全部";

    private static RectTransform Rect(string name, Transform parent)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.layer = 5;
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper)
    {
        rect.anchorMin = min; rect.anchorMax = max;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = lower; rect.offsetMax = upper;
    }

    private static void Stretch(RectTransform rect, float horizontal = 0, float vertical = 0) =>
        Anchor(rect, Vector2.zero, Vector2.one, new Vector2(horizontal, vertical), new Vector2(-horizontal, -vertical));

    private static UnityEngine.UI.Image Background(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private TMP_Text Text(Transform parent, string name, string value, int size, Color color)
    {
        var rect = Rect(name, parent);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Font;
        label.fontSize = size;
        label.text = value;
        label.color = color;
        label.richText = false;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.TopLeft;
        return label;
    }

    private UnityEngine.UI.Button MakeButton(Transform parent, string name, string text, UnityAction clicked)
    {
        var rect = Rect(name, parent);
        var image = Background(rect, Raised, true);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        button.onClick.AddListener(clicked);
        if (text.Length > 0)
        {
            var label = Text(rect, "Label", text, 24, Paper);
            Stretch(label.rectTransform, 10, 6);
            label.alignment = TextAlignmentOptions.Center;
        }
        SetButtonColor(button, Raised);
        return button;
    }

    private static void SetButtonColor(UnityEngine.UI.Button button, Color color)
    {
        button.GetComponent<UnityEngine.UI.Image>().color = color;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.23f, 1.20f, 1.15f, 1);
        colors.pressedColor = new Color(.75f, .75f, .75f, 1);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(.5f, .5f, .5f, 1);
        colors.fadeDuration = .08f;
        button.colors = colors;
    }
}
