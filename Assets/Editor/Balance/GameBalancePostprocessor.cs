using System;
using UnityEditor;

namespace MarblesECS.Editor
{
    internal sealed class GameBalancePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // AI builds must use the checked/edited JSON, even on a fresh Library import.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--game-ai-build") >= 0) return;
            foreach (string path in imported)
            {
                // Content Excel imports are explicit; saving the workbook must not overwrite GameContent.json.
                if (!string.Equals(path, GameBalanceImporter.WorkbookPath, StringComparison.Ordinal)) continue;
                EditorApplication.delayCall += GameBalanceImporter.ImportDefault;
                break;
            }
        }
    }
}
