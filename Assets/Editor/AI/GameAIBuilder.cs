#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static class GameAIBuilder
    {
        [Serializable] private sealed class SourceFile { public string path, sha256; }
        [Serializable] private sealed class Manifest { public string unityVersion, scene; public SourceFile[] files; }

        [MenuItem("Marbles ECS/AI/Build Training Player")]
        public static void Build()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string scene = EditorBuildSettings.scenes.FirstOrDefault(x => x.enabled && File.Exists(x.path))?.path;
            if (scene == null) throw new InvalidOperationException("Enable a gameplay scene in Build Settings.");
            string directory = Path.Combine(root, ".utmp", "game-ai", "player");
            Directory.CreateDirectory(directory);
            Debug.Log("AI player build started: " + scene);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { scene }, locationPathName = Path.Combine(directory, "GameAI.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("AI player build failed: " + report.summary.result);
            // Include import settings, native plugins and project physics settings as well as scripts.
            var extensions = new[] { ".cs", ".json", ".mres", ".unity", ".prefab", ".meta", ".asset",
                ".mat", ".physicMaterial", ".fbx", ".lua", ".dll", ".so" };
            var files = Directory.GetFiles(Path.Combine(root, "Assets"), "*", SearchOption.AllDirectories)
                .Where(x => extensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase) &&
                    !x.StartsWith(Path.Combine(root, "Assets", "AI", "Models") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("SceneDependencyCache"))
                .Concat(Directory.GetFiles(Path.Combine(root, "ProjectSettings"), "*", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(Path.Combine(root, "Packages"), "*.json"))
                .OrderBy(x => x, StringComparer.Ordinal).Select(x => new SourceFile {
                    path = x.Substring(root.Length + 1).Replace('\\', '/'), sha256 = Hash(File.ReadAllBytes(x)) }).ToArray();
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonUtility.ToJson(new Manifest {
                unityVersion = Application.unityVersion, scene = scene, files = files }, true));
            Debug.Log("AI player built at " + directory);
        }

        private static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif
