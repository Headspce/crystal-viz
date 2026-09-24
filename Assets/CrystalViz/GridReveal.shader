Shader "CrystalViz/GridReveal"
{
    // v1.0.40: the world-load grid. A dark plane with glowing grid lines that
    // sweeps in from the back of the world (_Wave1), then dissolves as the
    // real textured world grows in behind it (_Wave2). Purely aesthetic.
    Properties
    {
        _CellSize ("Grid Cell Size", Float) = 2.0
        _LineColor ("Grid Line Color", Color) = (0.25, 0.75, 1.0, 1)
        _BaseColor ("Grid Base Color", Color) = (0.015, 0.02, 0.06, 1)
        _Wave1 ("Grid Appear Wavefront Z", Float) = -10000
        _Wave2 ("Grid Dissolve Wavefront Z", Float) = -10000
        // v1.0.41: 0 = black & white grid, 1 = full line color. The reveal
        // sequencer fades it in as the world loads.
        _Colorize ("Grid Color Amount", Float) = 1.0
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Name "GridRevealUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            float _CellSize;
            half4 _LineColor;
            half4 _BaseColor;
            float _Wave1;
            float _Wave2;
            float _Colorize;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 wp = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = wp;
                output.positionHCS = TransformWorldToHClip(wp);
                output.fogFactor = ComputeFogFactor(output.positionHCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 wp = input.positionWS;

                // Procedural grid lines.
                float2 g = abs(frac(wp.xz / _CellSize) - 0.5);
                float gridLine = 1.0 - smoothstep(0.0, 0.045, min(g.x, g.y));

                // Wave 1: grid sweeps in from the back.
                float gridOn = 1.0 - smoothstep(_Wave1 - 1.0, _Wave1 + 1.0, wp.z);
                // Wave 2: grid dissolves as the real world arrives.
                float gone = smoothstep(_Wave2 - 1.5, _Wave2 + 1.5, wp.z);
                // Bright scanline riding the appear-wavefront.
                float band = (1.0 - smoothstep(0.0, 5.0, abs(wp.z - _Wave1))) * gridOn;

                // v1.0.41: black & white start, fading into the line color.
                half3 lineCol = lerp(half3(1.0, 1.0, 1.0), _LineColor.rgb, _Colorize);
                half3 col = _BaseColor.rgb + lineCol * (gridLine * 0.85 + band * 1.2);
                col = MixFog(col, input.fogFactor);
                float alpha = gridOn * (1.0 - gone);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
