Shader "CrystalViz/LeafWind"
{
    // Wind-blown tree leaves: alpha-cutout leaf quads that flutter and sway
    // in the same traveling gust fronts as the meadow grass (shared uniform
    // names and gust math with CrystalViz/StylizedGrass, so one weather
    // system drives both). Each leaf quad is small, so phasing the flutter
    // by object-space position makes every quad shiver rigidly like a real
    // leaf on its stem, pivoting near its base edge (uv.y).
    Properties
    {
        _BaseMap ("Leaf (cutout)", 2D) = "white" {}
        _WindStrength ("Wind Strength", Float) = 0.025
        _WindSpeed ("Wind Speed", Float) = 1.7
        _GustStrength ("Wind Gust Strength", Float) = 0.06
        _GustSpeed ("Wind Gust Speed", Float) = 1.8
        _GustFreq ("Wind Gust Frequency", Float) = 0.035
        _GustLighten ("Wind Gust Lighten", Float) = 0.28
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        // Leaf quads face random directions; render both sides.
        Cull Off

        Pass
        {
            Name "LeafForward"
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
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 positionWS  : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half   fogFactor   : TEXCOORD5;
                float  gust        : TEXCOORD6;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float _WindStrength;
            float _WindSpeed;
            float _GustStrength;
            float _GustSpeed;
            float _GustFreq;
            float _GustLighten;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);

                // Per-leaf phase from object-space position: all four verts
                // of one quad sit within centimeters, so each leaf flutters
                // as a rigid little sail.
                float3 op = IN.positionOS.xyz;
                float phase = op.x * 21.0 + op.y * 17.0 + op.z * 23.0;
                // High-frequency shiver layered over a slow sway, pivoting
                // near the leaf's base edge (uv.y) so tips dance most.
                float shiver = sin(_Time.y * 6.0 + phase) * 0.6
                             + sin(_Time.y * 9.7 + phase * 1.7) * 0.4;
                float swayPh = _Time.y * _WindSpeed + wp.x * 0.35 + wp.z * 0.27;
                float sway = sin(swayPh) * 0.6 + sin(swayPh * 2.3 + 1.7) * 0.4;
                float w = 0.55 + 0.45 * IN.uv.y;
                float2 flutter = float2(shiver * 0.5 + sway, (shiver * 0.35 - sway) * 0.6)
                               * _WindStrength * w;
                wp.x += flutter.x;
                wp.z += flutter.y;
                wp.y += shiver * 0.008 * w;

                // Same traveling gust fronts as the grass: leaves catch the
                // gust and lift as the bright band passes.
                float2 gustDir = normalize(float2(0.8, 0.6));
                float gustCoord = dot(wp.xz, gustDir) * _GustFreq - _Time.y * _GustSpeed;
                float gust = pow(0.5 + 0.5 * sin(gustCoord), 3.0);
                float gustB = pow(0.5 + 0.5 * sin(gustCoord * 0.41 + 2.1), 3.0);
                float gustAmt = gust * 0.75 + gustB * 0.25;
                wp.xz += gustDir * (w * _GustStrength * gustAmt);
                wp.y += w * _GustStrength * gustAmt * 0.2; // leaves lift in gusts
                OUT.gust = gustAmt;

                OUT.positionWS = wp;
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.shadowCoord = GetShadowCoord(vertexInput);

                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                // Alpha-tested cutout: the leaf silhouette from transparency.
                if (tex.a < 0.5) discard;
                // Near-white leaf albedo lets the per-leaf green vertex
                // colors define the hue.
                half3 albedo = tex.rgb * IN.color.rgb;
                // Gust bands catch the light, in sync with the meadow.
                albedo *= 1.0 + IN.gust * _GustLighten;

                Light mainLight = GetMainLight(IN.shadowCoord);
                // Wrapped diffuse: leaf quads face every direction; the wrap
                // keeps backfaces soft instead of black.
                half ndl = dot(IN.normalWS, mainLight.direction) * 0.5 + 0.5;
                half3 ambient = half3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
                half3 col = albedo * ambient;
                col += albedo * mainLight.color * ndl * mainLight.shadowAttenuation;

                uint extraCount = GetAdditionalLightsCount();
                for (uint i = 0; i < extraCount; ++i)
                {
                    Light l = GetAdditionalLight(i, IN.positionWS);
                    half d = dot(IN.normalWS, l.direction) * 0.5 + 0.5;
                    col += albedo * l.color * d * l.distanceAttenuation;
                }

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
