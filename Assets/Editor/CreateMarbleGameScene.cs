#if UNITY_EDITOR
using System;
using MarblesECS.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static class CreateMarbleGameScene
    {
        [MenuItem("Marbles ECS/Create Game Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string root = "Assets/Demo";
            if (!AssetDatabase.IsValidFolder(root)) AssetDatabase.CreateFolder("Assets", "Demo");
            string folder = AssetDatabase.GenerateUniqueAssetPath(root + "/Game");
            AssetDatabase.CreateFolder(root, folder.Substring(folder.LastIndexOf('/') + 1));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var game = new GameObject("Blood Marble Game").AddComponent<MarbleGameBootstrap>();
            game.BalanceJson = Resources.Load<TextAsset>("GameBalance");
            game.SurfaceMaterial = GameSceneAssets.CreateMaterial(folder, "GameSurface", Color.white);
            string path = folder + "/MarbleGame.unity";
            if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Could not save " + path);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = game.gameObject;
            Debug.Log("游戏场景已创建：" + path + "。按 Play 开始；发布时把此场景加入 Build Settings。");
        }

        [MenuItem("Marbles ECS/Create Game Scene", true)]
        private static bool CanCreate() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
#endif
