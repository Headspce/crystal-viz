Shader "Custom/cozyCove_water_WithDynamicRipples"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Layer1Tex ("Layer 1 (RGB)", 2D) = "white" {}
        _Layer2Tex ("Layer 2 (RGB)", 2D) = "white" {}
        _RippleTex ("Ripple Texture", 2D) = "white" {}
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.5
        _RippleSpeed ("Ripple Speed", Range(0, 1)) = 0.1
        _RippleFrequency ("Ripple Frequency", Range(0, 10)) = 3.0
        _Direction ("Flow Direction", Vector) = (1, 0, 0, 0)
        _FlowSpeed ("Flow Speed", Range(0, 1)) = 0.1
        _RippleCenter ("Ripple Center", Vector) = (0.5, 0.5, 0, 0)
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
            sampler2D _RippleTex;
            float4 _Direction;
            float _FlowSpeed;
            float _RippleStrength;
            float _RippleSpeed;
            float _RippleFrequency;
            float4 _RippleCenter;

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
                float3 worldPos : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv_MainTex = v.uv;
                o.uv_Layer1Tex = v.uv;
                o.uv_Layer2Tex = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // Calculate scrolling UVs for each layer
                float2 uv_MainTex = i.uv_MainTex + _FlowSpeed * _Time.y * _Direction.xy;
                float2 uv_Layer1Tex = i.uv_Layer1Tex + _FlowSpeed * 0.8 * _Time.y * _Direction.xy;
                float2 uv_Layer2Tex = i.uv_Layer2Tex + _FlowSpeed * 0.6 * _Time.y * _Direction.xy;

                // Sample the textures
                float4 baseColor = tex2D(_MainTex, uv_MainTex);
                float4 layer1Color = tex2D(_Layer1Tex, uv_Layer1Tex);
                float4 layer2Color = tex2D(_Layer2Tex, uv_Layer2Tex);

                // Combine the textures
                float4 combinedColor = baseColor * 0.5 + layer1Color * 0.3 + layer2Color * 0.2;

                // Calculate ripple effect
                float2 rippleCenter = _RippleCenter.xy;
                float distance = length(i.uv_MainTex - rippleCenter);
                float rippleEffect = sin(distance * _RippleFrequency - _Time.y * _RippleSpeed) * _RippleStrength;

                // Sample the ripple texture
                float4 rippleColor = tex2D(_RippleTex, uv_MainTex) * rippleEffect;

                // Apply ripple effect to the combined color
                combinedColor += rippleColor;

                return combinedColor;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
