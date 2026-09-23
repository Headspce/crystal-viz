Shader "CrystalViz/SpritePaper"
{
    // v1.0.35: dedicated unlit alpha-blend sprite shader for the Paper Mario
    // bees. The v1.0.34 sprite material used URP/Lit with
    // SetFloat("_AlphaClip", 1f) — but setting the float does NOT enable the
    // _ALPHATEST_ON shader keyword (only the editor's ShaderGUI syncs that),
    // so on the phone the quad rendered fully opaque and the transparent
    // texels showed as a black box around each bee. This shader needs no
    // keywords at all: exactly one variant, plain SrcAlpha blending, so there
    // is nothing for the stripper to remove and nothing to enable at runtime.
    // Pinned in the variant collection by CrystalVizBuild like the other
    // runtime-created shaders, so Shader.Find resolves on device.
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Name "SpritePaperUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return tex * _Color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
