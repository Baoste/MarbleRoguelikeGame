using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BloodPachinko.Rendering.Editor
{
    public static class RetroCultSetup
    {
        public const string ScenePath = "Assets/Scenes/TestScene.unity";
        public const string ProfilePath = "Assets/Art/Environments/BlackClinicV3/Lighting/BlackClinic_RetroCult.asset";
        public const string RootName = "画面效果_粗像素邪典";
        public static readonly string[] RendererPaths = {
            "Assets/Settings/URP-Performant-Renderer.asset",
            "Assets/Settings/URP-Balanced-Renderer.asset",
            "Assets/Settings/URP-HighFidelity-Renderer.asset"
        };

        [MenuItem("美术工具/后处理/选中粗像素邪典控制器")]
        public static void SelectControl()
        {
            var volume = UnityEngine.Object.FindObjectsOfType<Volume>(true)
                .FirstOrDefault(v => v.sharedProfile && v.sharedProfile.TryGet<RetroCultVolume>(out _));
            Selection.activeObject = volume ? volume.gameObject : AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (Selection.activeObject) EditorGUIUtility.PingObject(Selection.activeObject);
        }

        [MenuItem("美术工具/后处理/安装或修复当前场景的方案2")]
        public static void InstallCurrentScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出运行模式后安装。调节参数不需要重新安装。");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Rendering/RetroCult/RetroCult.shader");
            if (!shader) throw new InvalidOperationException("缺少 RetroCult.shader");
            foreach (var path in RendererPaths)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (!renderer) throw new InvalidOperationException("缺少 Renderer: " + path);
                var feature = renderer.rendererFeatures.OfType<RetroCultRendererFeature>().FirstOrDefault();
                if (!feature)
                {
                    feature = ScriptableObject.CreateInstance<RetroCultRendererFeature>();
                    feature.name = "粗像素邪典（由 Volume 调节）";
                    AssetDatabase.AddObjectToAsset(feature, renderer);
                    renderer.rendererFeatures.Add(feature);
                }
                feature.SetShader(shader);
                feature.SetActive(true);
                feature.Create();
                EditorUtility.SetDirty(feature);
                var serialized = new SerializedObject(renderer);
                var map = serialized.FindProperty("m_RendererFeatureMap");
                map.arraySize = renderer.rendererFeatures.Count;
                for (int i=0; i<map.arraySize; ++i)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long id);
                    map.GetArrayElementAtIndex(i).longValue = id;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                renderer.SetDirty();
                EditorUtility.SetDirty(renderer);
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "BlackClinic_RetroCult";
                AssetDatabase.CreateAsset(profile, ProfilePath);
                profile.Add<RetroCultVolume>(true).ApplySchemeTwo();
                profile.Add<Tonemapping>(true).mode.value = TonemappingMode.Neutral;
                var color = profile.Add<ColorAdjustments>(true);
                color.postExposure.value = .45f; color.contrast.value = 18; color.saturation.value = -24;
                var white = profile.Add<WhiteBalance>(true);
                white.temperature.value = 7; white.tint.value = 3;
                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.value = .22f; bloom.threshold.value = .95f; bloom.scatter.value = .55f;
                bloom.highQualityFiltering.value = true;
                var vignette = profile.Add<Vignette>(true);
                vignette.intensity.value = .20f; vignette.smoothness.value = .48f;
                profile.Add<ShadowsMidtonesHighlights>(true).shadows.value = new Vector4(.98f,.98f,1.02f,.01f);
                profile.Add<FilmGrain>(true).intensity.value = 0;
                profile.Add<ChromaticAberration>(true).intensity.value = 0;
                foreach (var component in profile.components)
                {
                    component.name = component is RetroCultVolume ? "粗像素邪典" : component.GetType().Name;
                    AssetDatabase.AddObjectToAsset(component, profile);
                }
                EditorUtility.SetDirty(profile);
            }
            var root = GameObject.Find(RootName);
            if (!root) { root = new GameObject(RootName); Undo.RegisterCreatedObjectUndo(root, "添加粗像素邪典"); }
            var volume = root.GetComponent<Volume>();
            if (!volume) volume = Undo.AddComponent<Volume>(root);
            volume.isGlobal = true; volume.priority = 10000; volume.weight = 1;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            SelectControl();
        }

        public static void InstallTestSceneBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            InstallCurrentScene();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("RETRO_CULT_INSTALL_OK");
        }

        public static void ValidateGameplayBatch()
        {
            InstallTestSceneBatch();
            MarblesECS.Editor.Diegetic.DeviceRoundTransitionValidation.Run();
        }
    }
}
