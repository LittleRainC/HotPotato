Shader "UI/HotPotatoCircularIris"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Center ("Center", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Radius", Range(0, 1)) = 0.25
        _Softness ("Edge Softness", Range(0.001, 0.1)) = 0.015
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 _Center;
            float _Radius;
            float _Softness;
            sampler2D _MainTex;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 delta = input.uv - _Center.xy;
                delta.x *= _ScreenParams.x / _ScreenParams.y;
                float distanceFromCenter = length(delta);
                float alpha = smoothstep(
                    _Radius - _Softness,
                    _Radius + _Softness,
                    distanceFromCenter);
                fixed textureAlpha = tex2D(_MainTex, input.uv).a;
                return fixed4(0, 0, 0, alpha * textureAlpha);
            }
            ENDCG
        }
    }
}
