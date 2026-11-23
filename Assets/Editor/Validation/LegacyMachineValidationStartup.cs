using MarblesECS.PhysX;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    [InitializeOnLoad]
    public static class LegacyMachineValidationStartup
    {
        private const string Pending = "MarblesECS.LegacyMachineValidationStartup";

        static LegacyMachineValidationStartup()
        {
            SceneManager.sceneLoaded += ConfigureScene;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.EraseBool(Pending);
            };
        }

        public static void EnterPlayMode()
        {
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void ConfigureScene(Scene scene, LoadSceneMode mode)
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(Pending, false) ||
                scene.path != "Assets/Scenes/TestScene.unity") return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var controller in root.GetComponentsInChildren<MarbleGameController>(true))
                    controller.StartWithDayCampaign = false;
            SessionState.EraseBool(Pending);
        }
    }
}
