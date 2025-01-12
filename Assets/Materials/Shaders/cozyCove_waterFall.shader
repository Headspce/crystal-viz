Shader "Custom/cozyCove_waterfall"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Layer1Tex ("Layer 1 (RGB)", 2D) = "white" {}
        _Layer2Tex ("Layer 2 (RGB)", 2D) = "white" {}
        _Direction ("Flow Direction", Vector) = (1, 0, 0, 0)
        _FlowSpeed ("Flow Speed", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Layer1Tex;
            sampler2D _Layer2Tex;
            float4 _Direction;
            float _FlowSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv_MainTex : TEXCOORD0;
                float2 uv_Layer1Tex : TEXCOORD1;
                float2 uv_Layer2Tex : TEXCOORD2;
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv_MainTex = v.uv;
                o.uv_Layer1Tex = v.uv;
                o.uv_Layer2Tex = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Calculate scrolling UVs for each layer
                float2 uv_MainTex = i.uv_MainTex + _FlowSpeed * _Time.y * _Direction.xy;
                float2 uv_Layer1Tex = i.uv_Layer1Tex + _FlowSpeed * 0.8 * _Time.y * _Direction.xy;
                float2 uv_Layer2Tex = i.uv_Layer2Tex + _FlowSpeed * 0.6 * _Time.y * _Direction.xy;

                // Sample the textures
                fixed4 baseColor = tex2D(_MainTex, uv_MainTex);
                fixed4 layer1Color = tex2D(_Layer1Tex, uv_Layer1Tex);
                fixed4 layer2Color = tex2D(_Layer2Tex, uv_Layer2Tex);

                // Combine the textures
                fixed4 combinedColor = baseColor * 0.5 + layer1Color * 0.3 + layer2Color * 0.2;

                return combinedColor;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
