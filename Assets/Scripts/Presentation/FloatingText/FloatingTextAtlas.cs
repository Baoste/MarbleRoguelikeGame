using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarblesECS
{
    internal readonly struct FloatingTextGlyph
    {
        internal readonly Vector4 UV;
        internal readonly float Width;

        internal FloatingTextGlyph(Vector4 uv, float width) { UV = uv; Width = width; }
    }

    internal sealed class FloatingTextAtlas
    {
        private const string RequiredGlyphs = "0123456789+-×.%!KMBTe";
        private readonly Dictionary<char, FloatingTextGlyph> glyphs;
        internal Material Material { get; }
        internal readonly float WidestGlyph;
        internal FloatingTextGlyph this[char character] => glyphs[character];

        internal static FloatingTextAtlas Load(FloatingTextConfig config)
        {
            var material = Resources.Load<Material>(config.MaterialPath);
            var metadata = Resources.Load<TextAsset>(config.GlyphsPath);
            if (!material || !metadata || !(material.mainTexture is Texture2D texture))
                throw new ArgumentException("Floating text requires a material with a Texture2D and glyph metadata.");
            return new FloatingTextAtlas(config, material, texture, ConfigJson.Read(metadata.text, metadata.name));
        }

        private FloatingTextAtlas(FloatingTextConfig config, Material material, Texture2D texture, ConfigFields fields)
        {
            Material = material;
            int width = texture.width, height = texture.height;
            if (fields.Int("width") != width || fields.Int("height") != height) throw fields.Error("texture dimensions");
            var entries = fields.Array("glyphs");
            glyphs = new Dictionary<char, FloatingTextGlyph>(entries.Count);
            foreach (var entry in entries)
            {
                var glyph = ConfigFields.From(entry, fields.Context + "/glyphs");
                string character = glyph.String("character");
                if (character.Length != 1 || glyphs.ContainsKey(character[0])) throw glyph.Error("character");
                int x = FloatingTextConfig.Integer(glyph, "x", 0, width - 2);
                int y = FloatingTextConfig.Integer(glyph, "y", 0, height - 2);
                int w = FloatingTextConfig.Integer(glyph, "width", 2, width - x);
                int h = FloatingTextConfig.Integer(glyph, "height", 2, height - y);
                float aspect = (float)FloatingTextConfig.Number(glyph, "advance", .05, 4, (double)w / h);
                // Metadata uses top-left pixels. Inset to texel centers for bilinear sampling.
                var uv = new Vector4((x + .5f) / width, (height - y - h + .5f) / height,
                    (x + w - .5f) / width, (height - y - .5f) / height);
                glyphs.Add(character[0], new FloatingTextGlyph(uv, aspect));
                WidestGlyph = Mathf.Max(WidestGlyph, aspect);
            }
            Require(RequiredGlyphs, fields);
            foreach (var style in config.Styles.Values)
            {
                Require(style.Prefix, fields);
                Require(style.Suffix, fields);
            }
        }

        private void Require(string characters, ConfigFields fields)
        {
            foreach (char character in characters)
                if (!glyphs.ContainsKey(character)) throw fields.Error("glyph '" + character + "'");
        }
    }
}
