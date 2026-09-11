using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarblesECS.Presentation
{
    /// <summary>Explicit render requests work when the player window has no visible swapchain.</summary>
    internal static class MarblePreviewRenderer
    {
        internal static Texture2D Capture(Camera camera, int width, int height)
        {
            if (camera == null) throw new InvalidOperationException("Preview camera was not found.");
            Rect previousRect = camera.rect;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = null;
            Texture2D image = null;
            bool completed = false;
            try
            {
                target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                camera.rect = new Rect(0, 0, 1, 1);
                camera.targetTexture = target;
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                else camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                image.Apply(false, false);
                completed = true;
                return image;
            }
            finally
            {
                camera.rect = previousRect;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (!completed && image != null) UnityEngine.Object.Destroy(image);
            }
        }
    }
}
