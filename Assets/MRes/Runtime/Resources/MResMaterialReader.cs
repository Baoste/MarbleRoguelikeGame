using System.Collections.Generic;
using UnityEngine;

namespace MRes
{
    internal sealed class MResMaterialData
    {
        internal string Name, ShaderId;
        internal int Queue;
        internal bool Instancing, DoubleSidedGI;
        internal string[] Keywords;
        internal readonly List<MResMaterialProperty> Properties = new List<MResMaterialProperty>();
    }
    internal sealed class MResMaterialProperty
    {
        internal string Name, Type, Texture;
        internal float Number;
        internal int Integer;
        internal Color Color;
        internal Vector4 Vector;
        internal Vector2 Scale = Vector2.one, Offset;
    }
    internal static class MResMaterialReader
    {
        internal static MResMaterialData Parse(MResFields f, MResPackage package)
        {
            var d = new MResMaterialData { Name = f.String("name", f.Context, true), ShaderId = f.String("shaderId"),
                Queue = f.Int("renderQueue", -1, true), Instancing = f.Bool("enableInstancing"), DoubleSidedGI = f.Bool("doubleSidedGI") };
            if (string.IsNullOrWhiteSpace(d.ShaderId) || d.Queue < -1 || d.Queue > 5000) throw f.Error("shaderId/renderQueue");
            var keywords = f.Array("keywords", true); d.Keywords = new string[keywords.Count];
            for (int i = 0; i < keywords.Count; i++)
            { if (!(keywords[i] is string s) || string.IsNullOrWhiteSpace(s)) throw f.Error("keywords"); d.Keywords[i] = s; }
            var names = new HashSet<string>();
            foreach (var item in f.Array("properties", true))
            {
                var p = MResFields.From(item, f.Context + "/properties");
                var v = new MResMaterialProperty { Name = p.String("name"), Type = p.String("type") };
                if (string.IsNullOrWhiteSpace(v.Name) || !names.Add(v.Name)) throw p.Error("name");
                switch (v.Type)
                {
                    case "Float": case "Range": v.Number = p.Float("value"); break;
                    case "Int": v.Integer = p.Int("value"); break;
                    case "Color": v.Color = MResValues.Color(p.Object("value")); break;
                    case "Vector": v.Vector = MResValues.V4(p.Object("value")); break;
                    case "Texture":
                        v.Texture = p.String("value", optional: true);
                        MResPackage.Reference(package.Textures, v.Texture, p.Context, true);
                        if (p.Has("scale")) v.Scale = MResValues.V2(p.Object("scale"));
                        if (p.Has("offset")) v.Offset = MResValues.V2(p.Object("offset"));
                        break;
                    default: throw p.Error("type");
                }
                d.Properties.Add(v);
            }
            return d;
        }
        internal static Material Create(MResMaterialData d, Shader shader, Dictionary<string, Texture2D> textures, MResLoadHandle handle)
        {
            var m = handle.Own(new Material(shader));
            m.name = d.Name; m.renderQueue = d.Queue; m.enableInstancing = d.Instancing;
            m.doubleSidedGI = d.DoubleSidedGI; m.shaderKeywords = d.Keywords;
            foreach (var p in d.Properties)
            {
                if (!m.HasProperty(p.Name)) throw new MResShaderException(d.ShaderId + " has no property " + p.Name);
                switch (p.Type)
                {
                    case "Float": case "Range": m.SetFloat(p.Name, p.Number); break;
                    case "Int": m.SetInteger(p.Name, p.Integer); break;
                    case "Color": m.SetColor(p.Name, p.Color); break;
                    case "Vector": m.SetVector(p.Name, p.Vector); break;
                    case "Texture":
                        m.SetTexture(p.Name, p.Texture == null ? null : textures[p.Texture]);
                        m.SetTextureScale(p.Name, p.Scale); m.SetTextureOffset(p.Name, p.Offset); break;
                }
            }
            return m;
        }
    }
}
