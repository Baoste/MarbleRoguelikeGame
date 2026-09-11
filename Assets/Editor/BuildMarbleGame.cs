#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static class BuildMarbleGame
    {
        private const string GameScene = "Assets/Demo/Game/MarbleGame.unity";

        [MenuItem("Marbles ECS/Build Windows Player")]
        public static void Build()
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "Demo/Game/MarbleGame.unity")))
                throw new FileNotFoundException("The playable campaign scene is missing.", GameScene);
            // Unity rejects player output inside Assets, including folders ending with '~'.
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/BloodMarble");
            Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "BloodMarble.exe");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GameScene },
                locationPathName = executable,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report == null) throw new InvalidOperationException("Unity did not produce a build report. Check the Editor log.");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result +
                    "; errors: " + report.summary.totalErrors + ". Check the Editor log.");
            Debug.Log("Windows player created: " + executable + " (" + report.summary.totalSize + " bytes)");
        }

        [MenuItem("Marbles ECS/Build Windows Player", true)]
        private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer;
    }
}
#endif
