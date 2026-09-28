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

            float hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = fract(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * vnoise(p);
                    p = p * 2.03 + 11.7;
                    a *= 0.5;
                }
                return v;
            }

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
                // Two domain-warped fbm layers drifting on different
                // vectors: the parallax keeps the bank from reading as a
                // flat scrolling texture.
                float2 q = IN.uv * 3.0 + _Seed;
                float2 warp = float2(
                    fbm(q * 1.2 + t * 0.045),
                    fbm(q * 1.2 - t * 0.038));
                float billow = fbm(q + warp * 0.9 + float2(t * 0.055, -t * 0.03));
                float detail = fbm(q * 2.3 - warp * 0.5 + float2(-t * 0.07, t * 0.05));
                float cloud = billow * 0.72 + detail * 0.28;

                // Subtle light variation: brighter patches where the light
                // breaks through, dimmer bellies underneath.
                float lum = 0.86 + 0.30 * fbm(q * 1.7 + float2(t * 0.03, t * 0.02) + 4.7);
                half3 col = _FogColor.rgb * lum;

                // Coverage: a high uniform base (nothing behind stays
                // readable at full cover) modulated by the billow so the
                // surface keeps soft cloudy structure instead of going flat.
                float a = _Cover * (0.90 + 0.10 * smoothstep(0.25, 0.75, cloud));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
