Shader "MResPacker/ExampleSurface"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Gain ("Gain", Float) = 1
        _Blend ("Blend", Range(0,1)) = 1
        _Offset ("Offset", Vector) = (0,0,0,0)
        _Mode ("Mode", Integer) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            sampler2D _BaseMap; float4 _BaseMap_ST; float4 _BaseColor;
            float _Gain, _Blend; float4 _Offset; int _Mode;
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _BaseMap); return o; }
            fixed4 frag(v2f i) : SV_Target { return tex2D(_BaseMap, i.uv + _Offset.xy) * _BaseColor * lerp(1, _Gain, _Blend); }
            ENDCG
        }
    }
}
