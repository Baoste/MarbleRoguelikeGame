using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        private AsyncOperation sceneUnload;
        private bool playerQuitPending;
        private int playerQuitCode;

        public void QuitGame(int exitCode = 0)
        {
            playerQuitCode = exitCode;
            Application.Quit(exitCode);
        }

        private void RegisterQuitHandler()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            Application.wantsToQuit += PreparePlayerQuit;
#endif
        }

        private void UnregisterQuitHandler()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            Application.wantsToQuit -= PreparePlayerQuit;
#endif
        }

        private bool PreparePlayerQuit()
        {
            if (playerQuitPending) return sceneUnload == null || sceneUnload.isDone;
            playerQuitPending = true;
            try { Cleanup(); }
            catch (Exception error) { Debug.LogException(error); return true; }
            if (sceneUnload == null || sceneUnload.isDone) return true;
            // Finish local PhysX scene disposal while the PlayerLoop is still running.
            sceneUnload.completed += OnSceneReleasedForQuit;
            return false;
        }

        private void OnSceneReleasedForQuit(AsyncOperation operation)
        {
            operation.completed -= OnSceneReleasedForQuit;
            Application.Quit(playerQuitCode);
        }
    }
}
