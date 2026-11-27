using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class ShopUIInstaller
{
    private const string FontPath = "Assets/Art/Fonts/ShopChineseSDF.asset";
    private const string FallbackPath = "Assets/Art/Fonts/ShopChineseFallback.asset";

    [MenuItem("Marbles ECS/UI/修复装置终端字体")]
    public static void Repair()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity");
        var shop = UnityEngine.Object.FindObjectOfType<ShopUI>(true);
        if (!shop || !shop.ShopCanvasGroup)
            throw new InvalidOperationException("Shop UI references are missing.");
        shop.TerminalFont = PrepareFont();
        EditorUtility.SetDirty(shop);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("SHOP_UI_REPAIR_PASSED: Chinese font and fallback assigned to the device terminal.");
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
        // Bake current content. The dynamic fallback covers future device names and descriptions.
        string characters = File.ReadAllText("Assets/Resources/GameContent.json") +
            "装置选择今日背包可选装置商店抽取公司工票用于抽取装置使用权限刷新返回房间确认背包" +
            "血肉残料安装加入移除已抽取工票不足必选上限普通罕见稀有传奇无限耐久·…◇" +
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

}
