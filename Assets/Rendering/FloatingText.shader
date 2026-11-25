Shader "Marbles/Floating Text"
{
    Properties
    {
        [MainTexture][NoScaleOffset] _MainTex ("Glyph Atlas (R Fill, G Silhouette)", 2D) = "black" {}
        _OutlineColor ("Outline", Color) = (0.06, 0.025, 0.02, 0.95)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+150" }
        Pass
        {
            Name "FloatingText"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half2 glyph = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rg;
                half outline = saturate(glyph.g - glyph.r) * _OutlineColor.a;
                return half4(input.color.rgb * glyph.r + _OutlineColor.rgb * outline, glyph.r + outline) * input.color.a;
            }
            ENDHLSL
        }
    }
}
