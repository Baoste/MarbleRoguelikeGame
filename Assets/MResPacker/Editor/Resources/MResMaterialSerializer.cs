using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MResPacker
{
    internal static class MResMaterialSerializer
    {
        internal static byte[] Serialize(Material material, MResBuildContext context)
        {
            var shader = material.shader;
            if (shader == null || !context.Shaders.TryGetValue(shader, out var id))
                throw new ArgumentException("Unregistered shader on material " + material.name + ": " + (shader == null ? "<missing>" : shader.name));
            if (!context.Report.ShaderIds.Contains(id)) context.Report.ShaderIds.Add(id);
            var properties = new List<object>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i); var type = shader.GetPropertyType(i);
                var p = MResJson.Object("name", name);
                switch (type)
                {
                    case ShaderPropertyType.Float: case ShaderPropertyType.Range:
                        p.Add("type", type.ToString()); p.Add("value", material.GetFloat(name)); break;
                    case ShaderPropertyType.Int:
                        p.Add("type", "Int"); p.Add("value", material.GetInteger(name)); break;
                    case ShaderPropertyType.Color:
                        p.Add("type", "Color"); p.Add("value", MResBuildContext.Color(material.GetColor(name))); break;
                    case ShaderPropertyType.Vector:
                        p.Add("type", "Vector"); p.Add("value", MResBuildContext.V4(material.GetVector(name))); break;
                    case ShaderPropertyType.Texture:
                        var texture = material.GetTexture(name);
                        if (shader.GetPropertyTextureDimension(i) != TextureDimension.Tex2D || (texture != null && !(texture is Texture2D)))
                            throw new ArgumentException("Only Texture2D properties are supported: " + material.name + "/" + name);
                        p.Add("type", "Texture"); p.Add("value", context.Texture(texture as Texture2D));
                        p.Add("scale", MResBuildContext.V2(material.GetTextureScale(name))); p.Add("offset", MResBuildContext.V2(material.GetTextureOffset(name))); break;
                    default: throw new ArgumentException("Unsupported shader property: " + name + " " + type);
                }
                properties.Add(p);
            }
            return MResJson.Bytes(MResJson.Object("name", material.name, "shaderId", id, "renderQueue", material.renderQueue,
                "enableInstancing", material.enableInstancing, "doubleSidedGI", material.doubleSidedGI,
                "keywords", material.shaderKeywords, "properties", properties));
        }
    }
}
