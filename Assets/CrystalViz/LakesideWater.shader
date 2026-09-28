Shader "CrystalViz/LakesideWater"
{
    Properties
    {
        _Color ("Tint", Color) = (0.16, 0.35, 0.45, 1)
        _Alpha ("Base Alpha", Range(0,1)) = 0.8
        _Mode ("0=Water 1=Mist", Float) = 0
    }
    SubShader
    {
        // v1.0.62: willow lakeside. One keyword-free shader, two modes:
        // 0 = pond water with drifting interference ripples and a radial
        //     shore fade so the rim melts into the grass.
        // 1 = soft drifting mist puff, denser at the center.
        // Transparent, unlit, no fog of its own. Pinned in the APK's
        // ShaderVariantCollection by CrystalVizBuild (single variant: no
        // multi_compile keywords), so Shader.Find resolves on device.
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            Name "LakesideWater"
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
                float3 positionWS  : TEXCOORD1;
            };

            half4 _Color;
            float _Alpha;
            float _Mode;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.uv = IN.uv;
                OUT.positionWS = wp;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_Mode < 0.5)
                {
                    float r1 = sin(IN.positionWS.x * 2.3 + _Time.y * 1.4)
                             * sin(IN.positionWS.z * 1.9 - _Time.y * 1.1);
                    float r2 = sin((IN.positionWS.x + IN.positionWS.z) * 3.7 - _Time.y * 0.7);
                    float ripple = r1 * 0.6 + r2 * 0.4; // -1..1
                    float hi = smoothstep(0.15, 0.95, ripple);
                    float edge = 1.0 - smoothstep(0.72, 0.98, length(IN.uv - 0.5) * 2.0);
                    half3 col = _Color.rgb * (0.82 + 0.28 * hi);
                    col += half3(0.35, 0.42, 0.45) * hi * 0.35; // sky glints
                    float a = _Alpha * (0.72 + 0.28 * hi) * edge;
                    return half4(col, a);
                }
                else
                {
                    float d = length(IN.uv - 0.5) * 2.0;
                    float a = _Alpha * pow(saturate(1.0 - d), 1.8);
                    return half4(_Color.rgb, a);
                }
            }
            ENDHLSL
        }
    }
}
