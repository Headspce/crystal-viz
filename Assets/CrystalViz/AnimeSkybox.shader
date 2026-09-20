Shader "CrystalViz/AnimeSkybox"
{
    // Procedural anime-style skybox: deep-blue gradient zenith melting into
    // a warm bright horizon, with big dramatic cel-shaded cumulus billows
    // and thin high cirrus wisps drifting ultra-slowly. Fully procedural, so
    // there is no texture seam anywhere no matter how long you stare at it.
    //
    // The horizon color is set from code to exactly match the scene fog
    // color, so the ground melts into the sky with no visible line.
    // _SunDir is synced from CrystalVizBootstrap.PlaceSun so the painted sun
    // disc follows the real directional light as it orbits.
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.15, 0.36, 0.78, 1)
        _MidColor ("Mid Sky Color", Color) = (0.45, 0.65, 0.93, 1)
        _HorizonColor ("Horizon Color", Color) = (0.611, 0.672, 0.824, 1)
        _CloudShadow ("Cloud Shadow", Color) = (0.70, 0.73, 0.87, 1)
        _CloudMid ("Cloud Mid", Color) = (0.93, 0.94, 0.99, 1)
        _CloudLight ("Cloud Highlight", Color) = (1.0, 1.0, 1.0, 1)
        _SunColor ("Sun Color", Color) = (1.0, 0.93, 0.78, 1)
        _SunDir ("Sun Direction", Vector) = (0.5, 0.5, -0.5, 0)
        _CloudScale ("Cloud Scale", Float) = 1.2
        _Coverage ("Cloud Coverage", Float) = 0.6
        _WindSpeed ("Cloud Drift Speed", Float) = 0.004
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Front
        ZWrite Off
        Fog { Mode Off }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
            };

            half4 _ZenithColor;
            half4 _MidColor;
            half4 _HorizonColor;
            half4 _CloudShadow;
            half4 _CloudMid;
            half4 _CloudLight;
            half4 _SunColor;
            float4 _SunDir;
            float _CloudScale;
            float _Coverage;
            float _WindSpeed;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dirOS = IN.positionOS.xyz;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
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
                for (int i = 0; i < 5; i++)
                {
                    v += a * vnoise(p);
                    p = p * 2.03 + float2(17.3, 9.1);
                    a *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dirOS);
                float y = d.y;

                // Sky gradient: bright warm horizon -> mid blue -> deep zenith.
                half3 sky = _HorizonColor.rgb;
                sky = lerp(sky, _MidColor.rgb, smoothstep(0.0, 0.28, y));
                sky = lerp(sky, _ZenithColor.rgb, smoothstep(0.22, 0.8, y));
                // Below the horizon hold the horizon color (the ground covers it).
                sky = lerp(_HorizonColor.rgb, sky, smoothstep(-0.08, 0.01, y));

                float3 sunDir = normalize(_SunDir.xyz);
                float sd = dot(d, sunDir);

                // Sun disc + layered halo.
                float disc = smoothstep(0.99925, 0.99965, sd);
                float glow = pow(saturate(sd), 700.0) * 1.4
                           + pow(saturate(sd), 90.0) * 0.5
                           + pow(saturate(sd), 8.0) * 0.18;
                sky += _SunColor.rgb * (disc * 2.2 + glow);

                // Dramatic anime cumulus: domain-warped billows, cel-shaded
                // into crisp shadow/mid/highlight bands.
                if (y > 0.015)
                {
                    float2 cuv = d.xz / (y + 0.22);
                    float2 drift = float2(_Time.y * _WindSpeed, _Time.y * _WindSpeed * 0.23);
                    float2 p = cuv * _CloudScale + drift;
                    float warp = fbm(p * 1.7 - drift * 0.5);
                    float cloudN = fbm(p + warp * 0.6);
                    float edge = 1.0 - _Coverage;
                    float m = smoothstep(edge, edge + 0.30, cloudN);
                    float horizonFade = smoothstep(0.015, 0.14, y);
                    if (m > 0.001)
                    {
                        float t = saturate((cloudN - edge) / 0.30); // 0 wispy edge -> 1 dense core
                        half3 cloudCol = _CloudShadow.rgb;
                        cloudCol = lerp(cloudCol, _CloudMid.rgb, smoothstep(0.18, 0.42, t));
                        cloudCol = lerp(cloudCol, _CloudLight.rgb, smoothstep(0.55, 0.85, t));
                        // Sun-kissed tops: warm lift on the lit side.
                        cloudCol += _SunColor.rgb * pow(saturate(sd), 3.0) * 0.22 * smoothstep(0.5, 1.0, t);
                        // Dense cores read brighter, thin edges cooler.
                        float shade = lerp(0.82, 1.12, t);
                        sky = lerp(sky, cloudCol * shade, m * horizonFade);
                    }

                    // High cirrus wisps, stretched thin.
                    float wisp = fbm(float2(p.x * 0.45 + 3.7, p.y * 2.4 - drift.y * 0.4));
                    float cirrus = smoothstep(0.60, 0.82, wisp) * 0.38 * smoothstep(0.12, 0.45, y);
                    sky = lerp(sky, half3(1.0, 1.0, 1.0), cirrus);
                }

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
