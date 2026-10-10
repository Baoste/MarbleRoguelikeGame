using System.IO;
using MRes.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MRes.Tests
{
    public static class MResSceneSetup
    {
        // Creates only the fixed demo fixture/scene. Never reads or exports a source prefab.
        [MenuItem("MRes/Recreate Acceptance Demo")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string folder = "Assets/MRes/Samples";
            const string bytesPath = folder + "/BouncePad.mres.bytes";
            const string registryPath = folder + "/DemoShaderRegistry.asset";
            File.WriteAllBytes(bytesPath, MResFixture.Create(MResFixture.BounceLua));
            AssetDatabase.ImportAsset(bytesPath);
            var registry = AssetDatabase.LoadAssetAtPath<MResShaderRegistry>(registryPath);
            if (registry == null)
            { registry = ScriptableObject.CreateInstance<MResShaderRegistry>(); AssetDatabase.CreateAsset(registry, registryPath); }
            registry.entries = new[] { new MResShaderRegistry.Entry { id = "MRes/Unlit", shader = Shader.Find("MRes/DemoUnlit") } };
            EditorUtility.SetDirty(registry);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("MRes Runtime Demo"); SceneManager.MoveGameObjectToScene(go, scene);
            var demo = go.AddComponent<MResDemo>(); demo.packageBytes = AssetDatabase.LoadAssetAtPath<TextAsset>(bytesPath);
            demo.shaderRegistry = registry;
            EditorSceneManager.SaveScene(scene, folder + "/MResRuntimeDemo.unity");
            AssetDatabase.SaveAssets();
            Debug.Log("MRES_DEMO_CREATED: " + folder + "/MResRuntimeDemo.unity");
        }
    }
}
