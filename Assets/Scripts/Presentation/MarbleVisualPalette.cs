using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS.Presentation
{
    internal sealed class MarbleVisualPalette : IDisposable
    {
        private readonly List<Material> materials = new List<Material>();
        public readonly Material Floor, Rail, Pin, Multiplier, Rush, Score, Random, Drain, Blood;

        public MarbleVisualPalette(Material template)
        {
            Floor = Make(template, "Board", new Color(.045f, .1f, .15f));
            Rail = Make(template, "Rails", new Color(.2f, .32f, .4f));
            Pin = Make(template, "Pins", new Color(.72f, .82f, .88f));
            Multiplier = Make(template, "Multiplier x2", new Color(1, .66f, .14f));
            Rush = Make(template, "Rush pin", new Color(.22f, .7f, 1));
            Score = Make(template, "Score", new Color(.12f, .72f, .5f));
            Random = Make(template, "Pending score", new Color(.66f, .3f, .92f));
            Drain = Make(template, "Drain", new Color(.35f, .15f, .22f));
            Blood = Make(template, "Blood marble", new Color(.95f, .13f, .2f));
        }

        private Material Make(Material template, string name, Color color)
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            string shaderName = pipeline == null ? "Standard" :
                pipeline.GetType().Name.Contains("HDRenderPipeline") ? "HDRP/Lit" : "Universal Render Pipeline/Lit";
            Shader shader = template != null ? template.shader : Shader.Find(shaderName);
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Scene needs a supported surface material.");
            var material = template != null ? new Material(template) : new Material(shader);
            material.name = name;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .35f);
            materials.Add(material);
            return material;
        }

        public void Dispose()
        {
            foreach (Material material in materials) UnityEngine.Object.Destroy(material);
            materials.Clear();
        }
    }
}
