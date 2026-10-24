Shader "Hidden/BloodPachinko/RetroCult"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Retro Cult"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _TargetSize;
            float _Strength, _VirtualHeight, _PixelBase, _PixelEdge, _Jitter, _Tick, _CellSize;
            float _EdgeThreshold, _EdgeSoftness;
            float _ColorSteps, _LumaSteps, _PosterMix, _Dither, _Grain, _Scanline, _Flicker;
            int _RectCount;
            float4 _ProtectedRects[48];

            float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float Luma(float3 c) { return dot(c, float3(.2126,.7152,.0722)); }
            float3 ToPerceptual(float3 c)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return c;
                #else
                    return LinearToSRGB(max(0, c));
                #endif
            }
            float3 FromPerceptual(float3 c)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return c;
                #else
                    return SRGBToLinear(c);
                #endif
            }
            float4 ReadSource(float2 uv)
            {
                // Work in viewport UVs, then map into the valid portion of an RTHandle.
                uv = clamp(uv, .5 * _TargetSize.zw, 1 - .5 * _TargetSize.zw);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv * _BlitScaleBias.xy + _BlitScaleBias.zw);
            }
            float3 ReadColor(float2 uv) { return ToPerceptual(ReadSource(uv).rgb); }
            float Bayer(float2 xy)
            {
                int x = (int)fmod(xy.x, 4), y = (int)fmod(xy.y, 4);
                float4 row = y==0 ? float4(0,8,2,10) : y==1 ? float4(12,4,14,6) : y==2 ? float4(3,11,1,9) : float4(15,7,13,5);
                return (row[x]+.5)/16-.5;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = (input.texcoord - _BlitScaleBias.zw) / _BlitScaleBias.xy;
                float4 source = ReadSource(uv);
                float3 original = ToPerceptual(source.rgb);
                float2 grid = float2(_VirtualHeight * _TargetSize.x / _TargetSize.y, _VirtualHeight);
                float2 unit = rcp(grid);
                float3 lr = ReadColor(uv+float2(unit.x,0)) - ReadColor(uv-float2(unit.x,0));
                float3 ud = ReadColor(uv+float2(0,unit.y)) - ReadColor(uv-float2(0,unit.y));
                float edge = smoothstep(_EdgeThreshold, _EdgeThreshold+_EdgeSoftness, max(length(lr),length(ud)));

                // URP renders this source to an intermediate RT with a flipped GPU projection
                // on top-origin APIs. Its sampled UVs already match WorldToViewportPoint.
                float2 textUV = uv;
                float protect = 0;
                [loop] for (int r=0; r<_RectCount; ++r)
                {
                    float4 rect = _ProtectedRects[r];
                    float2 d = min(textUV-rect.xy, rect.zw-textUV) * _TargetSize.xy;
                    protect = max(protect, smoothstep(-3,3,min(d.x,d.y)));
                }
                float2 cell = floor(uv*grid);
                float2 group = floor(cell/_CellSize);
                float2 jitter = (float2(Hash21(group+_Tick*13.17), Hash21(group+53.7+_Tick*7.93))-.5)*2*_Jitter;
                float2 snapped = (cell+.5)/grid;
                float2 moving = (floor(uv*grid+jitter)+.5)/grid;
                float3 c = lerp(original,ReadColor(snapped),_PixelBase*(1-protect));
                c = lerp(c,ReadColor(moving),edge*_PixelEdge*(1-protect));
                float threshold = Bayer(cell)*_Dither;
                float3 q = floor(c*_ColorSteps+.5+threshold)/_ColorSteps;
                float y = Luma(q);
                float quantY = max(.018,floor(y*_LumaSteps+.5+threshold)/_LumaSteps);
                q *= lerp(1,quantY/max(.025,y),.65);
                c = lerp(c,saturate(q),_PosterMix*(1-.94*protect));
                c += (Hash21(cell+_Tick*29.13)-.5)*_Grain*(.8-.5*Luma(c))*(1-protect);
                float scan = sin((floor(uv.y*grid.y)+.5)*PI);
                c *= 1-_Scanline*(scan*.5+.5)*(1-protect);
                c *= 1+_Flicker*(Hash21(float2(_Tick,17))-.5)*(1-protect);
                return float4(lerp(source.rgb,FromPerceptual(saturate(c)),_Strength),source.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
