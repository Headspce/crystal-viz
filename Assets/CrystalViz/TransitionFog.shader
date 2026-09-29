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
            };

            half4 _FogColor;
            float _Cover;
            float _Seed;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_Cover < 0.001) discard;

                float t = _Time.y;
                // Billowy cloud bank from layered domain-warped sine
                // fields, driven by SCREEN-SPACE position (guaranteed to
                // vary across the frame). Sines cannot collapse to a
                // constant: the bank always shows soft internal structure.
                float2 sp = IN.positionHCS.xy / _ScreenParams.xy; // 0..1
                float2 q = sp * 3.0 + _Seed;
                float w1 = sin(q.x * 1.5 + t * 0.04) + sin(q.y * 1.2 - t * 0.03);
                float w2 = sin(q.x * 1.1 - t * 0.05 + 2.0) + sin(q.y * 1.8 + t * 0.04 + 1.0);
                float billow = sin(q.x * 2.0 + w1 * 0.8 + t * 0.05)
                             * sin(q.y * 2.2 + w2 * 0.8 - t * 0.04);
                float detail = sin(q.x * 4.5 - w2 * 0.5 + t * 0.06 + 1.3)
                             * sin(q.y * 4.0 + w1 * 0.5 - t * 0.05 + 0.7);
                float cloud = billow * 0.65 + detail * 0.35; // ~[-1, 1]

                // Subtle light variation: brighter where the light breaks
                // through, dimmer bellies underneath. Kept pronounced
                // enough to read as cloud, not a flat panel.
                float lum = 0.85 + 0.30 * cloud;
                half3 col = _FogColor.rgb * lum;

                // Coverage: at full cover the bank is completely opaque —
                // nothing behind may stay readable while the swap happens.
                // The cloudy structure lives in the luminance variation
                // above, so full opacity still reads as cloud, not a panel.
                // During roll-in/out the alpha keeps soft billowy edges.
                float edge = smoothstep(-0.6, 0.6, cloud);
                float a = _Cover * (0.90 + 0.10 * edge);
                a = max(a, smoothstep(0.92, 1.0, _Cover));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
