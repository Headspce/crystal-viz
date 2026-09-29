Shader "CrystalViz/TransitionFog"
{
    // v1.0.63: the species-transition fog. Tyler's direction: NOT a flat
    // opaque whiteout (that would blind the player) — dense CLOUDY fog,
    // soft and billowy, like standing inside a drifting cloud bank. It
    // fully obscures the screen while the new environment swaps underneath,
    // but stays atmospheric: layered drifting noise, soft internal edges,
    // subtle light variation. Thick morning mist, not a flashbang.
    //
    // A fullscreen quad childed to the main camera wears this; the
    // EnvironmentManager drives _Cover 0 -> 1 (clouds roll in), holds it
    // while the world swaps, then 1 -> 0 (clouds part and drift away).
    // Tinted by _FogColor so the cloud bank belongs to the world it's
    // covering, not a neutral white.
    Properties
    {
        _FogColor ("Fog Tint", Color) = (0.75, 0.80, 0.87, 1)
        _Cover ("Cover 0-1", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
        _CloudTex ("Cloud Noise", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TransitionFog"
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
                float2 uv         : TEXCOORD0;
                float2 objXY      : TEXCOORD1;
            };

            half4 _FogColor;
            float _Cover;
            float _Seed;
            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                // Object-space XY of the quad (-0.5..0.5): guaranteed to
                // vary across the surface, independent of any uniforms.
                OUT.objXY = IN.positionOS.xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_Cover < 0.001) discard;

                // Billowy cloud bank: two samples of the CPU-generated
                // tileable cloud texture at different scales for parallax.
                // (Drift is driven by the C# side via _Seed animation;
                // _Time.y is unreliable in edit-mode captures.)
                float2 sp = IN.objXY + 0.5; // 0..1 across the quad
                float2 uv1 = sp * 1.5 + _Seed * 0.13;
                float2 uv2 = sp * 2.7 + _Seed * 0.29;
                float billow = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv1).r;
                float detail = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv2).r;
                float cloud = billow * 0.68 + detail * 0.32; // 0..1

                // Strong luminance variation for clearly visible billows:
                // dark bellies at 0.45x, bright lit tops at 1.55x.
                // (The earlier 0.78–1.22 range was too subtle on dark fog.)
                half3 col = _FogColor.rgb * (0.45 + 1.10 * cloud);

                // Coverage: at full cover the bank is completely opaque —
                // nothing behind may stay readable while the swap happens.
                // The cloudy structure lives in the luminance variation
                // above, so full opacity still reads as cloud, not a panel.
                // During roll-in/out the alpha keeps soft billowy edges.
                float edge = smoothstep(0.25, 0.75, cloud);
                float a = _Cover * (0.90 + 0.10 * edge);
                a = max(a, smoothstep(0.92, 1.0, _Cover));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
