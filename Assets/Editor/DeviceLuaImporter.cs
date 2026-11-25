using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MarblesECS.Editor
{
    [ScriptedImporter(1, "lua")]
    internal sealed class DeviceLuaImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            string source = File.ReadAllText(context.assetPath, Encoding.UTF8);
            var script = new TextAsset(source);
            context.AddObjectToAsset("script", script);
            context.SetMainObject(script);
        }
    }
}
