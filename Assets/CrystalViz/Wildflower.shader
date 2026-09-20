Shader "CrystalViz/Wildflower"
{
    // Wind-reactive wildflowers: same traveling-wave wind sway as the grass
    // field (bend grows toward the blossom, so stems stay planted), with the
    // stem painted by a green gradient and the blossom tinted by per-vertex
    // petal color. uv.x is 0 for stem verts, 1 for blossom verts; uv.y is
    // 0 at the root and 1 at the blossom for gradient + wind weighting.
    Properties
    {
        _RootColor ("Stem Root Color", Color) = (0.12, 0.30, 0.10, 1)
        _TipColor ("Stem Tip Color", Color) = (0.38, 0.64, 0.20, 1)
        _WindStrength ("Wind Strength", Float) = 0.035
        _WindSpeed ("Wind Speed", Float) = 1.7
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        // Thin quads: render both faces so a flower never vanishes edge-on.
        Cull Off

        Pass
        {
            Name "FlowerForward"
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
            };

            half4 _RootColor;
            half4 _TipColor;
            float _WindStrength;
            float _WindSpeed;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);

                // Same wind field as the grass so flowers and blades ripple
                // together: two layered sines, bend growing toward the
                // blossom (uv.y^2) so roots stay planted.
                float phase = _Time.y * _WindSpeed + wp.x * 0.35 + wp.z * 0.27;
                float sway = sin(phase) * 0.6 + sin(phase * 2.3 + 1.7) * 0.4;
                float bend = IN.uv.y * IN.uv.y * _WindStrength * sway;
                wp.x += bend;
                wp.z += bend * 0.4;

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
                half3 stem = lerp(_RootColor.rgb, _TipColor.rgb, IN.uv.y);
                // Petal color with a soft top-light lift so blossoms glow
                // a touch in the sun.
                half3 blossom = IN.color.rgb * (0.85 + 0.30 * IN.uv.y);
                half3 albedo = lerp(stem, blossom, step(0.5, IN.uv.x));

                Light mainLight = GetMainLight(IN.shadowCoord);
                // Wrapped diffuse keeps thin quads soft instead of black
                // on the unlit side.
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
