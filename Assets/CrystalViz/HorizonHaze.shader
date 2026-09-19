Shader "CrystalViz/HorizonHaze"
{
    Properties
    {
        _HazeColor ("Haze Color", Color) = (0.611, 0.672, 0.824, 1)
        _BottomAlpha ("Bottom Alpha", Float) = 0.9
    }
    SubShader
    {
        // Painted-smudge band that softens the line where the ground meets
        // the sky: a vertical gradient of fog-colored haze, strongest at the
        // ground line and dissolving upward. Transparent, unlit, no fog of
        // its own (it IS the haze), drawn after the opaque world.
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            Name "HorizonHaze"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            half4 _HazeColor;
            float _BottomAlpha;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Unity quad uv.y is 0 at the bottom: full haze at the ground
                // line, easing to nothing at the top — a soft painted blur,
                // not a hard edge.
                float a = _BottomAlpha * pow(1.0 - IN.uv.y, 1.6);
                return half4(_HazeColor.rgb, a);
            }
            ENDHLSL
        }
    }
}
