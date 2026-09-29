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
                // Start from the PROVEN working base (DEBUG): raw texture.
                // Add fog color tint and coverage with MINIMAL math.
                float2 sp = IN.objXY + 0.5;
                float c = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, sp).r;
                // Tint the grayscale clouds with the fog color, keeping
                // strong visible structure: lerp from dark to light.
                half3 col = _FogColor.rgb * (0.4 + 1.2 * c);
                // Simple alpha: _Cover directly, no smoothstep tricks.
                return half4(col, _Cover);
            }
            ENDHLSL
        }
    }
}
