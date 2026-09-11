using System;
using System.Collections;
using System.IO;
using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    /// <summary>Opt-in executable preview automation. Never attached during normal gameplay.</summary>
    public sealed class MarblePreviewCapture : MonoBehaviour
    {
        public MarbleGameController Controller;
        public static bool IsRequested => Array.IndexOf(Environment.GetCommandLineArgs(), "--marble-preview") >= 0;

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            double readyDeadline = Time.realtimeSinceStartupAsDouble + 20;
            while ((Controller == null || !Controller.IsReady) && Time.realtimeSinceStartupAsDouble < readyDeadline)
                yield return null;
            if (Controller == null || !Controller.IsReady || !BeginPreview())
            {
                Debug.LogError("Preview could not initialize the campaign.");
                if (Controller != null) Controller.QuitGame(2);
                else Application.Quit(2);
                yield break;
            }
            double captureTime = Time.realtimeSinceStartupAsDouble + 3;
            while (Time.realtimeSinceStartupAsDouble < captureTime)
            {
                // A hidden preview window can lose OS focus; this mode intentionally continues.
                if (Controller.Paused && !Controller.RequiresRestart) Controller.SetPaused(false);
                Controller.SetFireHeld(true);
                yield return null;
            }
            Controller.SetFireHeld(false);
            Controller.SetPaused(true);
            yield return new WaitForEndOfFrame();
            bool captured = SaveScreenshot();
            yield return new WaitForSecondsRealtime(1);
            Controller.QuitGame(captured ? 0 : 2);
        }

        private bool BeginPreview()
        {
            var devices = Controller.GetOwnedDevices();
            if (devices.Length > 0) Controller.PlaceDevice(devices[0].InstanceId, -.6f, 4.8f);
            if (devices.Length > 1) Controller.PlaceDevice(devices[devices.Length - 1].InstanceId, .6f, 4.8f);
            if (!Controller.BeginRound()) return false;
            Controller.AutoMoveLauncher = true;
            Controller.SetFlow(.25f);
            var drugs = Controller.GetDrugInventory();
            if (drugs.Length > 0 && drugs[0].Count > 0) Controller.UseDrug(drugs[0].DefinitionId);
            Controller.SetFireHeld(true);
            return true;
        }

        private static bool SaveScreenshot()
        {
            Texture2D texture = null;
            try
            {
                string path = GetOutputPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                texture = MarblePreviewRenderer.Capture(Camera.main,
                    Mathf.Clamp(Screen.width, 640, 2048), Mathf.Clamp(Screen.height, 640, 2048));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Debug.Log("Marble preview captured using an offscreen camera render (board only; no IMGUI): " + path);
                return true;
            }
            catch (Exception error) { Debug.LogException(error); return false; }
            finally { if (texture != null) Destroy(texture); }
        }

        private static string GetOutputPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--marble-preview-output");
            if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                return Path.GetFullPath(args[index + 1]);
            return Path.Combine(Application.persistentDataPath, "Game.png");
        }
    }
}
