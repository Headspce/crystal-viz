Shader "CrystalViz/AnimeSkyTextured"
{
    // Textured anime sky dome: samples an equirectangular painted sky
    // (bright puffy cumulus, rolling green hills at the horizon) by view
    // direction on the same giant inverted sphere the procedural sky used.
    // Direction comes from object space (the sphere is centered at its own
    // origin), so there is no UV seam anywhere on the mesh.
    Properties
    {
        _MainTex ("Sky (equirectangular)", 2D) = "white" {}
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dirOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dirOS);
                float u = atan2(d.z, d.x) / 6.2831853 + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) / 3.14159265 + 0.5;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(u, v));
            }
            ENDHLSL
        }
    }
}
