using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Repairs the existing shop in place; gameplay references and button bindings stay intact.
public static class ShopUIInstaller
{
    private const string FontPath = "Assets/Art/Fonts/ShopChineseSDF.asset";
    private const string FallbackPath = "Assets/Art/Fonts/ShopChineseFallback.asset";
    private static readonly Color Ink = new Color32(241, 234, 217, 255);
    private static readonly Color Muted = new Color32(184, 176, 158, 255);
    private static readonly Color Gold = new Color32(227, 192, 125, 255);

    [MenuItem("Marbles ECS/UI/修复商店布局与字体")]
    public static void Repair()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity");
        var shop = UnityEngine.Object.FindObjectOfType<ShopUI>(true);
        if (!shop || !shop.ShopCanvasGroup || !shop.OfferPrefab)
            throw new InvalidOperationException("Shop UI references are missing.");
        var font = PrepareFont();
        string prefabPath = AssetDatabase.GetAssetPath(shop.OfferPrefab);
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            RepairCard(prefab.GetComponent<ShopOfferItemUI>(), font);
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        RepairPanel(shop, font);
        EditorUtility.SetDirty(shop);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("SHOP_UI_REPAIR_PASSED: Chinese font, responsive layout and readable device cards.");
    }

    private static TMP_FontAsset PrepareFont()
    {
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/msyh.ttc");
        if (!source) throw new InvalidOperationException("Chinese source font is missing.");
        FontEngine.InitializeFontEngine();
        var font = FontAsset(FontPath, source, 2048);
        var fallback = FontAsset(FallbackPath, source, 1024);
        var settings = new SerializedObject(fallback);
        settings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        fallback.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fallback.isMultiAtlasTexturesEnabled = true;
        font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
        // Bake current content; the small dynamic fallback covers future MRes names and descriptions.
        string characters = File.ReadAllText("Assets/Resources/GameContent.json") +
            "装置商店选择装置，为下一回合做好准备当前金币刷新商品完成购买去布置每件限购一次，后可在阶段放置。" +
            "成功已加入背包失败请检查库存容量或状态不足无法已售出普通稀有史诗传奇无限耐久出售·…□" +
            new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray());
        characters = new string(characters.Where(c => !char.IsControl(c)).Distinct().ToArray());
        characters = new string(characters.Where(c => !font.HasCharacter(c, false, false)).ToArray());
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        // Restore the runtime source explicitly when extending a previously saved static atlas.
        var fontSettings = new SerializedObject(font);
        fontSettings.FindProperty("m_SourceFontFile").objectReferenceValue = source;
        fontSettings.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = source;
        fontSettings.ApplyModifiedPropertiesWithoutUndo();
        font.isMultiAtlasTexturesEnabled = true;
        if (characters.Length > 0 && !font.TryAddCharacters(characters, out string missing))
            throw new InvalidOperationException("Shop font cannot render: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach (var texture in font.atlasTextures)
            if (texture && !AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
        EditorUtility.SetDirty(font);
        EditorUtility.SetDirty(fallback);
        return font;
    }

    private static TMP_FontAsset FontAsset(string path, Font source, int atlasSize)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font) return font;
        font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA,
            atlasSize, atlasSize, AtlasPopulationMode.Dynamic, true);
        font.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(font, path);
        font.material.name = font.name + " Material";
        AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var texture in font.atlasTextures)
        {
            texture.name = font.name + " Atlas";
            AssetDatabase.AddObjectToAsset(texture, font);
        }
        return font;
    }

    private static void RepairPanel(ShopUI shop, TMP_FontAsset font)
    {
        var panel = (RectTransform)shop.ShopCanvasGroup.transform;
        var canvas = panel.GetComponentInParent<Canvas>();
        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;
        canvas.sortingOrder = 100;
        Stretch(panel, Vector2.zero, Vector2.zero);
        Paint(panel, new Color32(17, 19, 19, 250), true);

        var title = Label(panel, "ShopTitle", font, 46, Gold);
        title.text = "装置商店";
        Box(title.rectTransform, new Vector2(0, 1), new Vector2(96, -64), new Vector2(900, 66));
        var subtitle = Label(panel, "ShopSubtitle", font, 24, Muted);
        subtitle.text = "选择装置，为下一回合做好准备";
        Box(subtitle.rectTransform, new Vector2(0, 1), new Vector2(98, -140), new Vector2(900, 40));

        var coins = (RectTransform)shop.CoinsText.transform.parent;
        DisableLayout(coins);
        Box(coins, new Vector2(1, 1), new Vector2(-96, -72), new Vector2(360, 78));
        Paint(coins, new Color32(37, 36, 30, 255), false);
        var caption = coins.GetComponentsInChildren<TMP_Text>(true).First(t => t != shop.CoinsText);
        Style(caption, font, 22, Muted);
        Box(caption.rectTransform, new Vector2(0, .5f), new Vector2(24, 0), new Vector2(110, 44));
        caption.text = "当前金币";
        Style(shop.CoinsText, font, 36, Gold, TextAlignmentOptions.Right);
        Stretch(shop.CoinsText.rectTransform, new Vector2(140, 12), new Vector2(-24, -12));
        shop.CoinsText.enableAutoSizing = true;
        shop.CoinsText.fontSizeMin = 22;
        shop.CoinsText.fontSizeMax = 36;

        var offers = (RectTransform)shop.OfferContainer;
        Stretch(offers, new Vector2(96, 206), new Vector2(-96, -246));
        Paint(offers, Color.clear, false);
        var layout = offers.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.padding = new RectOffset();
        layout.spacing = 24;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        var refresh = (RectTransform)shop.RefreshButton.transform.parent;
        DisableLayout(refresh);
        Box(refresh, new Vector2(1, 0), new Vector2(-452, 88), new Vector2(316, 64));
        Stretch((RectTransform)shop.RefreshButton.transform, Vector2.zero, Vector2.zero);
        foreach (var label in shop.RefreshButton.GetComponentsInChildren<TMP_Text>(true))
            if (label != shop.RefreshCostText) label.gameObject.SetActive(false);
        shop.RefreshCostText.transform.SetParent(shop.RefreshButton.transform, false);
        Stretch(shop.RefreshCostText.rectTransform, new Vector2(12, 8), new Vector2(-12, -8));
        Style(shop.RefreshCostText, font, 25, Ink, TextAlignmentOptions.Center);
        shop.RefreshCostText.text = "刷新商品 · 5 金币";
        ButtonStyle(shop.RefreshButton, new Color32(58, 59, 51, 255));

        Box((RectTransform)shop.ContinueButton.transform, new Vector2(1, 0), new Vector2(-96, 88), new Vector2(332, 64));
        ButtonStyle(shop.ContinueButton, new Color32(123, 53, 42, 255));
        var next = shop.ContinueButton.GetComponentInChildren<TMP_Text>(true);
        Style(next, font, 26, Ink, TextAlignmentOptions.Center);
        Stretch(next.rectTransform, new Vector2(16, 8), new Vector2(-16, -8));
        next.text = "完成购买 · 去布置";
        shop.FeedbackText = Label(panel, "ShopFeedback", font, 23, Muted);
        shop.FeedbackText.text = "每件商品限购一次，购买后可在布置阶段放置。";
        var feedback = shop.FeedbackText.rectTransform;
        feedback.anchorMin = Vector2.zero; feedback.anchorMax = new Vector2(1, 0);
        feedback.pivot = Vector2.zero;
        feedback.offsetMin = new Vector2(96, 84); feedback.offsetMax = new Vector2(-820, 156);
        foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            label.font = font; label.fontSharedMaterial = font.material;
            label.raycastTarget = false;
        }
    }

    private static void RepairCard(ShopOfferItemUI item, TMP_FontAsset font)
    {
        var card = (RectTransform)item.transform;
        Paint(card, new Color32(37, 36, 32, 255), false);
        var layout = Component<UnityEngine.UI.LayoutElement>(card);
        layout.minWidth = 0; layout.preferredWidth = 400; layout.flexibleWidth = 1;
        layout.minHeight = 0; layout.preferredHeight = -1; layout.flexibleHeight = 1;
        Style(item.KindText, font, 22, Muted);
        Top(item.KindText.rectTransform, 24, 34);
        Style(item.NameText, font, 34, Ink, TextAlignmentOptions.TopLeft);
        Top(item.NameText.rectTransform, 66, 82);
        item.MetadataText = Label(card, "MetadataText", font, 21, Muted);
        item.MetadataText.text = "普通 · 耐久 20 · 出售 5 金币";
        Top(item.MetadataText.rectTransform, 156, 32);
        var divider = Rect(card, "DescriptionDivider");
        Top(divider, 199, 1);
        Paint(divider, new Color32(78, 75, 63, 255), false);

        var scrollRoot = Rect(card, "DescriptionScroll");
        Stretch(scrollRoot, new Vector2(24, 177), new Vector2(-24, -222));
        var viewport = Rect(scrollRoot, "Viewport");
        Stretch(viewport, Vector2.zero, new Vector2(-12, 0));
        Paint(viewport, Color.white, true);
        Component<UnityEngine.UI.Mask>(viewport).showMaskGraphic = false;
        var content = item.DescriptionText.rectTransform;
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0, 1); content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        Style(item.DescriptionText, font, 23, Ink, TextAlignmentOptions.TopLeft);
        item.DescriptionText.lineSpacing = 6;
        item.DescriptionText.overflowMode = TextOverflowModes.Overflow;
        var fitter = Component<UnityEngine.UI.ContentSizeFitter>(content);
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        Component<UnityEngine.UI.LayoutElement>(content).layoutPriority = 2;
        var scroll = Component<UnityEngine.UI.ScrollRect>(scrollRoot);
        scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30; scroll.inertia = false;
        var track = Rect(scrollRoot, "Scrollbar");
        track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(-5, 0); track.offsetMax = Vector2.zero;
        Paint(track, new Color32(60, 59, 51, 255), true);
        var handle = Rect(track, "Handle"); Stretch(handle, Vector2.zero, Vector2.zero);
        var thumb = Paint(handle, new Color32(150, 136, 107, 255), true);
        var scrollbar = Component<UnityEngine.UI.Scrollbar>(track);
        scrollbar.handleRect = handle; scrollbar.targetGraphic = thumb;
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        item.DescriptionScroll = scroll;

        Style(item.PriceText, font, 32, Gold);
        Bottom(item.PriceText.rectTransform, 113, 48);
        Style(item.SoldText, font, 20, Muted);
        Bottom(item.SoldText.rectTransform, 80, 30);
        item.SoldText.text = "已加入背包"; item.SoldText.enabled = false;
        Bottom((RectTransform)item.BuyButton.transform, 24, 54);
        ButtonStyle(item.BuyButton, new Color32(123, 53, 42, 255));
        item.BuyLabel = item.BuyButton.GetComponentInChildren<TMP_Text>(true);
        Style(item.BuyLabel, font, 25, Ink, TextAlignmentOptions.Center);
        Stretch(item.BuyLabel.rectTransform, new Vector2(12, 6), new Vector2(-12, -6));
        item.BuyLabel.text = "购买装置";
        EditorUtility.SetDirty(item);
    }

    private static T Component<T>(RectTransform target) where T : UnityEngine.Component =>
        target.GetComponent<T>() ?? target.gameObject.AddComponent<T>();
    private static RectTransform Rect(Transform parent, string name)
    {
        var found = parent.Find(name) as RectTransform;
        if (found) return found;
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); return rect;
    }
    private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size, Color color)
    {
        var label = Component<TextMeshProUGUI>(Rect(parent, name));
        Style(label, font, size, color); return label;
    }
    private static void Style(TMP_Text text, TMP_FontAsset font, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        text.font = font; text.fontSharedMaterial = font.material; text.fontSize = size;
        text.enableAutoSizing = false; text.fontStyle = FontStyles.Normal;
        text.alignment = alignment; text.color = color; text.raycastTarget = false;
        text.enableWordWrapping = true; text.richText = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.margin = Vector4.zero; text.characterSpacing = 0; text.lineSpacing = 0;
    }
    private static UnityEngine.UI.Image Paint(RectTransform rect, Color color, bool raycast)
    {
        var image = Component<UnityEngine.UI.Image>(rect);
        image.sprite = null; image.color = color; image.raycastTarget = raycast;
        return image;
    }
    private static void ButtonStyle(UnityEngine.UI.Button button, Color color)
    {
        button.targetGraphic = Paint((RectTransform)button.transform, color, true);
        button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(.8f, .8f, .8f);
        colors.disabledColor = new Color(.5f, .5f, .5f, .75f);
        colors.fadeDuration = .1f; button.colors = colors;
    }
    private static void DisableLayout(RectTransform rect)
    {
        var layout = rect.GetComponent<UnityEngine.UI.LayoutGroup>(); if (layout) layout.enabled = false;
    }
    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = min; rect.offsetMax = max; rect.localScale = Vector3.one;
    }
    private static void Box(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one;
    }
    private static void Top(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0, 1);
        rect.offsetMin = new Vector2(24, -top - height); rect.offsetMax = new Vector2(-24, -top);
    }
    private static void Bottom(RectTransform rect, float bottom, float height)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1, 0); rect.pivot = Vector2.zero;
        rect.offsetMin = new Vector2(24, bottom); rect.offsetMax = new Vector2(-24, bottom + height);
    }
}
