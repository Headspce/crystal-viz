Shader "CrystalViz/MeadowGround"
{
    // Meadow ground: dark-green base (exact tint preserved) with subtle
    // Poly Haven dirt detail multiplied in, plus soft procedural cloud
    // shadows drifting along the grass wind direction (0.8, 0.6).
    //
    // The dirt texture is sampled as a DETAIL layer: its luminance modulates
    // the base color rather than replacing it, so the ground keeps its exact
    // dark-green tint while gaining earthy variation. Cloud shadows are
    // two-octave value noise scrolled along the wind vector, darkening the
    // ground softly like passing clouds.
    Properties
    {
        _BaseColor ("Base Color (dark moss green)", Color) = (0.24, 0.45, 0.17, 1)
        _DirtTex ("Dirt Detail (Poly Haven brown_mud_leaves)", 2D) = "white" {}
        _DirtTiling ("Dirt Tiling", Float) = 24.0
        _DirtStrength ("Dirt Detail Strength", Range(0, 1)) = 0.35
        _CloudShadowStrength ("Cloud Shadow Strength", Range(0, 1)) = 0.28
        _CloudShadowScale ("Cloud Shadow Scale", Float) = 0.08
        _CloudShadowSpeed ("Cloud Shadow Speed", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "MeadowForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ FOG_LINEAR FOG_EXP2

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half   fogFactor   : TEXCOORD4;
            };

            half4 _BaseColor;
            TEXTURE2D(_DirtTex);
            SAMPLER(sampler_DirtTex);
            float _DirtTiling;
            float _DirtStrength;
            float _CloudShadowStrength;
            float _CloudShadowScale;
            float _CloudShadowSpeed;

            // Wind direction shared with StylizedGrass.shader: normalize(0.8, 0.6)
            static const float2 WIND_DIR = float2(0.8, 0.6) * 1.25; // 1/0.8 = 1.25 normalizes

            // Hash for value noise
            float hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            // Two-octave value noise for soft cloud shapes
            float cloudNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));

                float n1 = lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);

                // Second octave for detail
                float2 p2 = p * 2.13 + 17.7;
                float2 i2 = floor(p2);
                float2 f2 = frac(p2);
                float2 u2 = f2 * f2 * (3.0 - 2.0 * f2);
                float n2 = lerp(
                    lerp(hash21(i2), hash21(i2 + float2(1, 0)), u2.x),
                    lerp(hash21(i2 + float2(0, 1)), hash21(i2 + float2(1, 1)), u2.x),
                    u2.y);

                return n1 * 0.65 + n2 * 0.35;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 wp = mul(unity_ObjectToWorld, IN.positionOS);
                OUT.positionWS = wp.xyz;
                OUT.shadowCoord = TransformWorldToShadowCoord(wp.xyz);
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Base dark moss green (exact tint preserved)
                half3 col = _BaseColor.rgb;

                // Dirt detail: sample tiled texture, use luminance as modulation.
                // The dirt texture is brown; we extract its brightness variation
                // and multiply it into the green base so the hue stays green
                // while gaining earthy texture.
                float2 dirtUV = IN.positionWS.xz * (_DirtTiling / 200.0);
                half3 dirt = SAMPLE_TEXTURE2D(_DirtTex, sampler_DirtTex, dirtUV).rgb;
                float dirtLum = dot(dirt, half3(0.299, 0.587, 0.114));
                // Center the modulation around 1.0: darker dirt darkens, lighter lightens
                float dirtMod = lerp(1.0, dirtLum * 1.6, _DirtStrength);
                col *= dirtMod;

                // Cloud shadows: procedural noise scrolling along wind direction
                float2 windDir = normalize(float2(0.8, 0.6));
                float2 cloudUV = IN.positionWS.xz * _CloudShadowScale + windDir * _Time.y * _CloudShadowSpeed;
                float cloud = cloudNoise(cloudUV);
                // Sharpen slightly for distinct cloud shapes, keep soft edges
                cloud = smoothstep(0.35, 0.75, cloud);
                col *= 1.0 - cloud * _CloudShadowStrength;

                // Simple lambert lighting with main light
                Light mainLight = GetMainLight(IN.shadowCoord);
                half3 normal = normalize(IN.normalWS);
                half ndotl = saturate(dot(normal, mainLight.direction));
                half3 lit = col * (mainLight.color * ndotl + SampleSH(normal));

                // Apply fog
                lit = MixFog(lit, IN.fogFactor);

                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // Shadow caster pass so the ground can receive shadows properly
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
