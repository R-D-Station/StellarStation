Shader "Station/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineSize ("Outline Size", Float) = 1
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _OutlineColor;
            float _OutlineSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                if (c.a >= 0.1)
                    return c * i.color;

                float2 t = _MainTex_TexelSize.xy * _OutlineSize;
                float a = tex2D(_MainTex, i.uv + float2(t.x, 0)).a;
                a = max(a, tex2D(_MainTex, i.uv - float2(t.x, 0)).a);
                a = max(a, tex2D(_MainTex, i.uv + float2(0, t.y)).a);
                a = max(a, tex2D(_MainTex, i.uv - float2(0, t.y)).a);
                a = max(a, tex2D(_MainTex, i.uv + t).a);
                a = max(a, tex2D(_MainTex, i.uv - t).a);
                a = max(a, tex2D(_MainTex, i.uv + float2(t.x, -t.y)).a);
                a = max(a, tex2D(_MainTex, i.uv + float2(-t.x, t.y)).a);
                if (a >= 0.1)
                    return _OutlineColor;

                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
