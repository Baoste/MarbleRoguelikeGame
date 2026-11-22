// Adapted from QuickOutline by Chris Nolet, copyright © 2018 Chris Nolet.

Shader "Marbles/QuickOutline/Fill"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth("Outline Width (Pixels)", Range(0, 10)) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+110" "RenderType" = "Transparent" }
        Pass
        {
            Name "OutlineFill"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZTest Always
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            Stencil
            {
                Ref 8
                ReadMask 8
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            float4 Vert(float3 positionOS : POSITION, float3 smoothNormal : TEXCOORD3) : SV_POSITION
            {
                float4 positionCS = TransformObjectToHClip(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(smoothNormal);
                float3 normalVS = TransformWorldToViewDir(normalWS);
                float2 direction = normalVS.xy * float2(1, _ProjectionParams.x);
                positionCS.xy += direction * (2 * _OutlineWidth) * positionCS.w / GetScaledScreenParams().xy;
                return positionCS;
            }

            half4 Frag() : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
