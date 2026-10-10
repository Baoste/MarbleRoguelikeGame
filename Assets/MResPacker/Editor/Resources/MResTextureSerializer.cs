using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MResPacker
{
    internal static class MResTextureSerializer
    {
        internal static byte[] Serialize(Texture2D source, out Dictionary<string, object> settings)
        {
            if (source.width < 1 || source.height < 1 || source.width > 8192 || source.height > 8192 || (long)source.width * source.height > 16777216)
                throw new ArgumentException("Texture exceeds v1 dimensions: " + source.name);
            bool linear = !source.isDataSRGB;
            settings = MResJson.Object("name", source.name, "linear", linear, "mipChain", source.mipmapCount > 1,
                "wrapModeU", (int)source.wrapModeU, "wrapModeV", (int)source.wrapModeV, "filterMode", (int)source.filterMode, "anisoLevel", source.anisoLevel);
            Texture2D copy = null; RenderTexture render = null;
            var previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
            try
            {
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, linear);
                if (source.isReadable && (source.format == TextureFormat.RGBA32 || source.format == TextureFormat.ARGB32 || source.format == TextureFormat.RGB24))
                { copy.SetPixels32(source.GetPixels32()); copy.Apply(false, false); }
                else
                {
                    if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                        throw new ArgumentException("GPU readback is required for texture " + source.name + ". Run Unity with graphics enabled (without -nographics).");
                    render = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                        linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                    GL.sRGBWrite = !linear && QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(source, render); RenderTexture.active = render;
                    copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); copy.Apply(false, false);
                }
                return copy.EncodeToPNG();
            }
            finally
            {
                GL.sRGBWrite = previousSrgb; RenderTexture.active = previous;
                if (render != null) RenderTexture.ReleaseTemporary(render);
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }
        }
    }
}
