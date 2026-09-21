Shader "CrystalViz/StylizedGrass"
{
    Properties
    {
        _RootColor ("Root Color", Color) = (0.15, 0.34, 0.11, 1)
        _TipColor ("Tip Color", Color) = (0.58, 0.82, 0.26, 1)
        _WindStrength ("Wind Strength", Float) = 0.16
        _WindSpeed ("Wind Speed", Float) = 1.7
        _GustStrength ("Wind Gust Strength", Float) = 0.38
        _GustSpeed ("Wind Gust Speed", Float) = 1.8
        _GustFreq ("Wind Gust Frequency", Float) = 0.06
        _GustLighten ("Wind Gust Lighten", Float) = 0.28
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        // Blades are thin single-sided strips; render both faces so a tuft
        // never vanishes when viewed edge-on.
        Cull Off

        Pass
        {
            Name "GrassForward"
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
                float  gust        : TEXCOORD5;
            };

            half4 _RootColor;
            half4 _TipColor;
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

                // Wind sway: two layered sines drifting across the field, with
                // bend growing toward the blade tip (uv.y^2) so roots stay planted.
                float phase = _Time.y * _WindSpeed + wp.x * 0.35 + wp.z * 0.27;
                float sway = sin(phase) * 0.6 + sin(phase * 2.3 + 1.7) * 0.4;
                float tipW = IN.uv.y * IN.uv.y;
                float bend = tipW * _WindStrength * sway;
                wp.x += bend;
                wp.z += bend * 0.4;

                // Zelda-style traveling gust fronts: a sharpened wave band
                // sweeping across the field, combing the blades flat in the
                // gust direction as it passes. Two detuned bands keep the
                // rhythm from looking mechanical.
                float2 gustDir = normalize(float2(0.8, 0.6));
                float gustCoord = dot(wp.xz, gustDir) * _GustFreq - _Time.y * _GustSpeed;
                float gust = pow(0.5 + 0.5 * sin(gustCoord), 3.0);
                float gustB = pow(0.5 + 0.5 * sin(gustCoord * 0.41 + 2.1), 3.0);
                float gustAmt = gust * 0.75 + gustB * 0.25;
                wp.xz += gustDir * (tipW * _GustStrength * gustAmt);
                OUT.gust = gustAmt;

                OUT.positionWS = wp;
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.shadowCoord = GetShadowCoord(vertexInput);

                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Stylized gradient: dark root blending to a light sunny tip.
                half3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, IN.uv.y);

                // Coherent patchiness: large soft patches of lighter/darker
                // grass drifting across the field, Zelda-meadow style. Pure
                // function of world position, so it costs no vertex data.
                float patch = sin(IN.positionWS.x * 0.11 + IN.positionWS.z * 0.07)
                            * sin(IN.positionWS.x * 0.05 - IN.positionWS.z * 0.13);
                albedo *= 0.85 + 0.30 * (0.5 + 0.5 * patch);
                // The gust band also catches the light: a bright wave visibly
                // sweeping the meadow, Breath-of-the-Wild style.
                albedo *= 1.0 + IN.gust * _GustLighten;

                Light mainLight = GetMainLight(IN.shadowCoord);
                // Wrapped diffuse: blades are up-normaled, so a plain NdotL
                // would shade one side black; the wrap keeps it soft and sunny.
                half ndl = dot(IN.normalWS, mainLight.direction) * 0.5 + 0.5;
                // Flat ambient probe (RenderSettings.ambientMode = Flat).
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
