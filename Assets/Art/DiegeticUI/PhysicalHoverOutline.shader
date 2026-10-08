Shader "Marbles/Physical Hover Outline"
{
    Properties { _OutlineColor ("Highlight",Color)=(1,0.68,0.3,1) _Width ("World width",Float)=0.025 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" "RenderType"="Opaque" }
        Pass
        {
            Cull Front ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _OutlineColor; float _Width;
            CBUFFER_END
            struct A { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct V { float4 pos:SV_POSITION; };
            V vert(A a){V o;float3 w=TransformObjectToWorld(a.vertex.xyz);w+=TransformObjectToWorldNormal(a.normal)*_Width;o.pos=TransformWorldToHClip(w);return o;}
            half4 frag(V i):SV_Target{return _OutlineColor;}
            ENDHLSL
        }
    }
}
