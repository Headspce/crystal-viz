Shader "CrystalViz/ScrollingClouds"
{
    Properties
    {
        _MainTex ("Cloud Texture", 2D) = "white" {}
        _ScrollSpeed ("Scroll Speed", Float) = 0.008
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        // Drawn early among transparents so the glass sphere blends over it.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
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
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _ScrollSpeed;
            float4 _Tint;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Scroll left -> right: sample point drifts left in UV space.
                float2 uv = IN.uv;
                uv.x = frac(uv.x - _Time.y * _ScrollSpeed);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                return c * _Tint;
            }
            ENDHLSL
        }
    }
}
