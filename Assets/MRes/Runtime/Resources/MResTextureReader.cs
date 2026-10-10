using System;
using UnityEngine;

namespace MRes
{
    internal sealed class MResTextureData
    {
        internal byte[] Png;
        internal string Name;
        internal bool MipChain = true, Linear;
        internal TextureWrapMode WrapU, WrapV;
        internal FilterMode Filter = FilterMode.Bilinear;
        internal int Aniso = 1;
    }

    internal static class MResTextureReader
    {
        internal static MResTextureData Parse(byte[] png, string path, MResFields f)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (png.Length < 33) throw new MResFormatException("Truncated PNG: " + path);
            for (int i = 0; i < signature.Length; i++)
                if (png[i] != signature[i]) throw new MResFormatException("Expected PNG: " + path);
            if (Big(png, 8) != 13 || Big(png, 12) != 0x49484452 || Big(png, 16) == 0 || Big(png, 20) == 0 ||
                Big(png, 16) > MResFormat.MaxTextureDimension || Big(png, 20) > MResFormat.MaxTextureDimension ||
                (ulong)Big(png, 16) * Big(png, 20) > 16777216)
                throw new MResFormatException("Invalid/oversized PNG IHDR: " + path);
            bool data = false, end = false;
            int cursor = 8;
            while (cursor < png.Length)
            {
                if (png.Length - cursor < 12) throw new MResFormatException("Truncated PNG chunk: " + path);
                uint length = Big(png, cursor), type = Big(png, cursor + 4);
                if (length > png.Length - cursor - 12) throw new MResFormatException("PNG chunk outside entry: " + path);
                if (type == 0x49444154) data = true;
                cursor += (int)length + 12;
                if (type == 0x49454E44) { end = length == 0 && cursor == png.Length; break; }
            }
            if (!data || !end) throw new MResFormatException("PNG missing IDAT/IEND: " + path);
            var d = new MResTextureData { Png = png, Name = path };
            if (f != null)
            {
                d.Name = f.String("name", path, true); d.MipChain = f.Bool("mipChain", true); d.Linear = f.Bool("linear");
                d.WrapU = MResValues.Enum<TextureWrapMode>(f, "wrapModeU", 0);
                d.WrapV = MResValues.Enum<TextureWrapMode>(f, "wrapModeV", 0);
                d.Filter = MResValues.Enum<FilterMode>(f, "filterMode", 1); d.Aniso = f.Int("anisoLevel", 1, true);
                if (d.Aniso < 0 || d.Aniso > 16) throw f.Error("anisoLevel");
            }
            return d;
        }
        private static uint Big(byte[] b, int i) => (uint)b[i] << 24 | (uint)b[i + 1] << 16 | (uint)b[i + 2] << 8 | b[i + 3];
        internal static Texture2D Create(MResTextureData d, MResLoadHandle handle)
        {
            var texture = handle.Own(new Texture2D(2, 2, TextureFormat.RGBA32, d.MipChain, d.Linear));
            if (!texture.LoadImage(d.Png, false)) throw new MResInstantiationException("PNG decoding failed: " + d.Name);
            texture.name = d.Name; texture.wrapModeU = d.WrapU; texture.wrapModeV = d.WrapV;
            texture.filterMode = d.Filter; texture.anisoLevel = d.Aniso;
            return texture;
        }
    }

    internal static class MResValues
    {
        internal static T Enum<T>(MResFields f, string key, int fallback) where T : struct
        {
            int n = f.Int(key, fallback, true);
            if (!System.Enum.IsDefined(typeof(T), n)) throw f.Error(key);
            return (T)System.Enum.ToObject(typeof(T), n);
        }
        internal static Vector2 V2(MResFields f) => new Vector2(f.Float("x"), f.Float("y"));
        internal static Vector3 V3(MResFields f) => new Vector3(f.Float("x"), f.Float("y"), f.Float("z"));
        internal static Vector4 V4(MResFields f) => new Vector4(f.Float("x"), f.Float("y"), f.Float("z"), f.Float("w"));
        internal static Color Color(MResFields f) => new Color(f.Float("r"), f.Float("g"), f.Float("b"), f.Float("a"));
        internal static float Nonnegative(MResFields f, string key, float fallback)
        {
            float v = f.Float(key, fallback, true); if (v < 0) throw f.Error(key); return v;
        }
    }
}
