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

                // DEBUG2: output cloud value to isolate the combination.
                float2 sp = IN.objXY + 0.5;
                float2 uv1 = sp * 1.5 + _Seed * 0.13;
                float2 uv2 = sp * 2.7 + _Seed * 0.29;
                float billow = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv1).r;
                float detail = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv2).r;
                float cloud = billow * 0.68 + detail * 0.32;
                return half4(cloud, cloud, cloud, 1.0);
            }
            ENDHLSL
        }
    }
}
