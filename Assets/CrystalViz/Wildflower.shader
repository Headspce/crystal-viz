Shader "CrystalViz/Wildflower"
{
    // Wind-reactive wildflowers: same traveling-wave wind sway as the grass
    // field (bend grows toward the blossom, so stems stay planted), with the
    // stem painted by a green gradient and the blossom wearing a daisy-like
    // petal-head texture (alpha cutout) tinted by per-vertex petal color.
    // uv.x is 0 for stem verts, 1 for blossom verts; uv.y is 0 at the root
    // and 1 at the blossom for gradient + wind weighting; uv1 carries the
    // blossom-head texture coordinates.
    Properties
    {
        _RootColor ("Stem Root Color", Color) = (0.12, 0.30, 0.10, 1)
        _TipColor ("Stem Tip Color", Color) = (0.38, 0.64, 0.20, 1)
        _BlossomMap ("Blossom Head Map", 2D) = "white" {}
        _WindStrength ("Wind Strength", Float) = 0.035
        _WindSpeed ("Wind Speed", Float) = 1.7
        // Shared gust fronts with the grass field (identical defaults) so
        // flowers and blades ripple in sync when a gust band passes.
        _GustStrength ("Wind Gust Strength", Float) = 0.30
        _GustSpeed ("Wind Gust Speed", Float) = 1.8
        _GustFreq ("Wind Gust Frequency", Float) = 0.06
        _GustLighten ("Wind Gust Lighten", Float) = 0.28
        _GrowFront ("Reveal Wavefront Z", Float) = 10000
        _GrowWidth ("Reveal Wavefront Width", Float) = 3
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
                float2 uv1        : TEXCOORD1;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 uv1         : TEXCOORD1;
                float4 color       : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
                float3 positionWS  : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                half   fogFactor   : TEXCOORD6;
                float  gust        : TEXCOORD7;
            };

            half4 _RootColor;
            half4 _TipColor;
            TEXTURE2D(_BlossomMap);
            SAMPLER(sampler_BlossomMap);
            float _WindStrength;
            float _WindSpeed;
            float _GustStrength;
            float _GustSpeed;
            float _GustFreq;
            float _GustLighten;
            float _GrowFront;
            float _GrowWidth;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);

                // World-reveal wave (v1.0.40): same as the grass — flowers
                // stay collapsed until the wavefront sweeps past their Z.
                float growT = 1.0 - smoothstep(_GrowFront - _GrowWidth,
                                              _GrowFront + _GrowWidth, wp.z);
                float grow = growT * growT * (3.0 - 2.0 * growT);
                wp.y *= grow;

                // Same wind field as the grass so flowers and blades ripple
                // together: two layered sines, bend growing toward the
                // blossom (uv.y^2) so roots stay planted.
                float phase = _Time.y * _WindSpeed + wp.x * 0.35 + wp.z * 0.27;
                float sway = sin(phase) * 0.6 + sin(phase * 2.3 + 1.7) * 0.4;
                float tipW = IN.uv.y * IN.uv.y;
                float bend = tipW * _WindStrength * sway * grow;
                wp.x += bend;
                wp.z += bend * 0.4;

                // Zelda-style traveling gust fronts, shared with the grass
                // field: the same sharpened wave bands sweep the flowers,
                // bending blossoms in the gust direction as they pass.
                float2 gustDir = normalize(float2(0.8, 0.6));
                float gustCoord = dot(wp.xz, gustDir) * _GustFreq - _Time.y * _GustSpeed;
                // v1.0.29: x*x*x instead of pow(x, 3.0) — same curve, cheaper ALU.
                float gs = 0.5 + 0.5 * sin(gustCoord);
                float gust = gs * gs * gs;
                float gsB = 0.5 + 0.5 * sin(gustCoord * 0.41 + 2.1);
                float gustB = gsB * gsB * gsB;
                float gustAmt = gust * 0.75 + gustB * 0.25;
                wp.xz += gustDir * (tipW * _GustStrength * gustAmt * grow);
                OUT.gust = gustAmt;

                OUT.positionWS = wp;
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.uv = IN.uv;
                OUT.uv1 = IN.uv1;
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
                // Blossom: petal-head texture (alpha cutout) tinted by the
                // per-flower petal vertex color, with a soft top-light lift
                // so blossoms glow a touch in the sun.
                float isBlossom = step(0.5, IN.uv.x);
                half4 blossomTex = SAMPLE_TEXTURE2D(_BlossomMap, sampler_BlossomMap, IN.uv1);
                if (isBlossom > 0.5) clip(blossomTex.a - 0.5);
                half3 blossom = IN.color.rgb * blossomTex.rgb * (0.85 + 0.30 * IN.uv.y);
                half3 albedo = lerp(stem, blossom, isBlossom);
                // The gust band catches the light here too, in sync with the
                // grass: a bright wave visibly sweeping the blossoms.
                albedo *= 1.0 + IN.gust * _GustLighten;

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
