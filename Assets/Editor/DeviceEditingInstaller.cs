using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DeviceEditingInstaller
{
    [MenuItem("Marbles ECS/UI/配置装置移动与售出区")]
    public static void ConfigureCurrentScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var placement = roots.SelectMany(g => g.GetComponentsInChildren<DevicePlacementUI>(true)).Single();
        var existing = roots.SelectMany(g => g.GetComponentsInChildren<DeviceSellAreaUI>(true)).SingleOrDefault();
        Undo.RecordObject(placement, "绑定装置售出区");
        if (existing)
        {
            Undo.RecordObject(existing, "绑定装置放置流程");
            existing.Placement = placement;
            placement.SellArea = existing;
            EditorUtility.SetDirty(existing);
        }
        else
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/ShopChineseSDF.asset");
            if (!font) throw new InvalidOperationException("现有中文字体缺失。");
            var canvasObject = new GameObject("DeviceEditCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObject, "创建设备售出界面");
            canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var panel = new GameObject("DeviceSellArea", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(CanvasGroup), typeof(DeviceSellAreaUI));
            panel.layer = 5;
            var rect = (RectTransform)panel.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 20);
            rect.sizeDelta = new Vector2(700, 94);
            existing = panel.GetComponent<DeviceSellAreaUI>();
            existing.Placement = placement;
            existing.Group = panel.GetComponent<CanvasGroup>();
            existing.Background = panel.GetComponent<UnityEngine.UI.Image>();
            existing.Background.color = new Color32(104, 27, 35, 240);
            existing.Background.raycastTarget = true;
            existing.Title = Label(rect, "Title", font, "装置售出区", -13, 30, 24);
            existing.Hint = Label(rect, "Hint", font, "左键选中装置并移动  ·  再次左键放置  ·  R 旋转  ·  右键取消", -51, 27, 18);
            existing.Hint.color = new Color32(255, 216, 213, 255);
            placement.SellArea = existing;
        }
        EditorUtility.SetDirty(placement);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static TMP_Text Label(RectTransform parent, string name, TMP_FontAsset font, string value,
        float top, float height, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, top);
        rect.sizeDelta = new Vector2(-32, height);
        var label = go.GetComponent<TMP_Text>();
        label.font = font; label.fontSharedMaterial = font.material;
        label.fontSize = label.fontSizeMax = fontSize; label.fontSizeMin = fontSize - 3;
        label.enableAutoSizing = true; label.enableWordWrapping = false;
        label.alignment = TextAlignmentOptions.Center; label.color = Color.white;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false; label.raycastTarget = false; label.text = value;
        return label;
    }
}
