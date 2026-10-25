using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Moves the existing RUSH UI into a screen overlay and adds the score-stack timer.</summary>
public static class StatusHudInstaller
{
    [MenuItem("Marbles ECS/UI/配置右上角状态进度条")]
    public static void ConfigureCurrentScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var listener = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ScoreListener>(true)).Single();
        if (!listener.RushText || !listener.RushSlider || !listener.RushSlider.fillRect)
            throw new InvalidOperationException("现有 RUSH 文字、进度条或填充引用缺失。");
        var canvas = listener.RushSlider.GetComponentInParent<Canvas>(true);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/ShopChineseSDF.asset");
        if (!canvas || !font || listener.RushText.GetComponentInParent<Canvas>(true) != canvas)
            throw new InvalidOperationException("现有 RUSH Canvas 或中文字体缺失。");

        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "配置状态进度条");
        Undo.RecordObject(listener, "绑定状态进度条");
        Undo.SetTransformParent(canvas.transform, null, "移动状态 Canvas");
        canvas.name = "StatusHudCanvas";
        canvas.gameObject.layer = 5;
        canvas.transform.localScale = Vector3.one;
        canvas.transform.localRotation = Quaternion.identity;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.overrideSorting = false;
        canvas.sortingOrder = 50;
        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        if (!scaler) scaler = Undo.AddComponent<UnityEngine.UI.CanvasScaler>(canvas.gameObject);
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;

        var panel = canvas.transform.Find("StatusBars") as RectTransform;
        if (!panel)
        {
            var go = new GameObject("StatusBars", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(go, "创建状态面板");
            panel = (RectTransform)go.transform;
            panel.SetParent(canvas.transform, false);
        }
        panel.gameObject.layer = 5;
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
        // Leave room for the existing top-right item-library button.
        panel.anchoredPosition = new Vector2(-28, -132);
        panel.sizeDelta = new Vector2(420, 164);
        panel.localScale = Vector3.one;
        panel.localRotation = Quaternion.identity;
        var background = panel.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color32(23, 24, 22, 235);
        background.raycastTarget = false;
        var group = panel.GetComponent<CanvasGroup>();
        group.alpha = 1;
        group.interactable = group.blocksRaycasts = false;
        listener.StatusHud = group;

        listener.RushText.transform.SetParent(panel, false);
        listener.RushText.name = "RushStatus";
        StyleText(listener.RushText, font, new Color32(244, 199, 120, 255));
        TopRect(listener.RushText.rectTransform, 18, -14, 384, 30);
        listener.RushText.text = $"RUSH  0 / {MarblesECS.RushSystem.Threshold:0}";
        listener.RushSlider.transform.SetParent(panel, false);
        listener.RushSlider.name = "RushProgress";
        TopRect((RectTransform)listener.RushSlider.transform, 18, -52, 384, 12);
        StyleBar(listener.RushSlider, new Color32(228, 161, 74, 255));

        if (!listener.ScoreStackText)
        {
            var go = UnityEngine.Object.Instantiate(listener.RushText.gameObject, panel);
            Undo.RegisterCreatedObjectUndo(go, "创建叠加状态文字");
            listener.ScoreStackText = go.GetComponent<TMP_Text>();
        }
        listener.ScoreStackText.name = "ScoreStackStatus";
        StyleText(listener.ScoreStackText, font, new Color32(149, 222, 202, 255));
        TopRect(listener.ScoreStackText.rectTransform, 18, -90, 384, 30);
        listener.ScoreStackText.text = "得分叠加 +0.00  ·  等待进球";
        if (!listener.ScoreStackSlider)
        {
            var go = UnityEngine.Object.Instantiate(listener.RushSlider.gameObject, panel);
            Undo.RegisterCreatedObjectUndo(go, "创建叠加倒计时条");
            listener.ScoreStackSlider = go.GetComponent<UnityEngine.UI.Slider>();
        }
        listener.ScoreStackSlider.name = "ScoreStackProgress";
        TopRect((RectTransform)listener.ScoreStackSlider.transform, 18, -128, 384, 12);
        StyleBar(listener.ScoreStackSlider, new Color32(96, 192, 167, 255));
        foreach (var graphic in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        EditorUtility.SetDirty(listener);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void StyleText(TMP_Text text, TMP_FontAsset font, Color color)
    {
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontSize = 22;
        text.enableAutoSizing = true;
        text.fontSizeMin = 18;
        text.fontSizeMax = 22;
        text.fontStyle = FontStyles.Normal;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Truncate;
        text.margin = Vector4.zero;
        text.raycastTarget = false;
        text.enabled = true;
    }

    private static void StyleBar(UnityEngine.UI.Slider slider, Color fill)
    {
        slider.gameObject.SetActive(true);
        slider.minValue = 0; slider.maxValue = 1; slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(0);
        slider.interactable = false;
        slider.transition = UnityEngine.UI.Selectable.Transition.None;
        slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.handleRect = null;
        foreach (var image in slider.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            image.raycastTarget = false;
            image.color = new Color32(55, 59, 53, 255);
        }
        var fillImage = slider.fillRect.GetComponent<UnityEngine.UI.Image>();
        if (!fillImage) throw new InvalidOperationException("进度条填充图像缺失。");
        fillImage.color = fill;
        var area = slider.fillRect.parent as RectTransform;
        if (area && area != slider.transform) Stretch(area);
        foreach (Transform child in slider.transform)
            if (child != slider.fillRect && child.GetComponent<UnityEngine.UI.Image>()) Stretch((RectTransform)child);
        slider.fillRect.offsetMin = slider.fillRect.offsetMax = Vector2.zero;
    }

    private static void TopRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        rect.anchoredPosition3D = new Vector3(x, y, 0);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
