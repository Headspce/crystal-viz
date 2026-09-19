Shader "CrystalViz/StylizedGrass"
{
    Properties
    {
        _RootColor ("Root Color", Color) = (0.15, 0.34, 0.11, 1)
        _TipColor ("Tip Color", Color) = (0.58, 0.82, 0.26, 1)
        _WindStrength ("Wind Strength", Float) = 0.16
        _WindSpeed ("Wind Speed", Float) = 1.7
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
            };

            half4 _RootColor;
            half4 _TipColor;
            float _WindStrength;
            float _WindSpeed;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);

                // Wind sway: two layered sines drifting across the field, with
                // bend growing toward the blade tip (uv.y^2) so roots stay planted.
                float phase = _Time.y * _WindSpeed + wp.x * 0.35 + wp.z * 0.27;
                float sway = sin(phase) * 0.6 + sin(phase * 2.3 + 1.7) * 0.4;
                float bend = IN.uv.y * IN.uv.y * _WindStrength * sway;
                wp.x += bend;
                wp.z += bend * 0.4;

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
