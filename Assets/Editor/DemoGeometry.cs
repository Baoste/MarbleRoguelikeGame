#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    /// <summary>Asset helpers used by the production game-scene generator.</summary>
    internal static class GameSceneAssets
    {
        internal static Material CreateMaterial(string folder, string name, Color color)
        {
            UnityEngine.Rendering.RenderPipelineAsset pipeline =
                UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            string shaderName = pipeline == null ? "Standard" :
                pipeline.GetType().Name.Contains("HDRenderPipeline") ? "HDRP/Lit" :
                "Universal Render Pipeline/Lit";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException("No supported Lit shader was found.");

            Material material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, folder + "/" + name + ".mat");
            return material;
        }

    }
}
#endif
