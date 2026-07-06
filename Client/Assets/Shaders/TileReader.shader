Shader "Custom/TileReader"
{
    Properties
    {
        [MainTexture] _TopMap("Base Map", 2D) = "white"
        [MainTexture] _DownTex("_DawnTex", 2D) = "black"{}
        _cur("_cur",Float)=0
        _cur_corner("_cur_corner",Float)=0
        _rotate("_rotate",Float)=0
        _count_x("_count_x",Float)=0
        _count_y("_count_y",Float)=0
        _alpha("_alpha",Float)=0


    }

    SubShader
    {
        Tags { "RenderType" = "Transperent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_TopMap);
            SAMPLER(sampler_TopMap);
            TEXTURE2D(_DownTex);
            SAMPLER(sampler_DownTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _TopMap_ST;
                float4 _DownTex_ST;
                uint _cur;
                uint _cur_corner;
                uint _rotate;
                uint _count_x;
                uint _count_y;
                uint _alpha;
                
            CBUFFER_END

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
            float2 rotateUv(float2 uv){
                static const float2x2 R[4]={
                    float2x2(1,0,0,1),
                    float2x2(0,-1,1,0),
                    float2x2(-1,0,0,-1),
                    float2x2(0,1,-1,0)
                };
                return mul(R[_rotate],uv - 0.5) + 0.5;
            };
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv,_TopMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 tiledUV = IN.uv;

                tiledUV.x=(tiledUV.x+_cur-1)/_count_x;
                tiledUV.y=(tiledUV.y+1)/_count_y;
                half4 downColor = SAMPLE_TEXTURE2D( _DownTex, sampler_DownTex, tiledUV);
                half4 wallTex = SAMPLE_TEXTURE2D( _TopMap, sampler_TopMap, tiledUV);

                tiledUV = rotateUv(IN.uv);
                tiledUV.y=(tiledUV.y)/_count_y;  
                tiledUV.x=(tiledUV.x+_cur_corner-1)/_count_x;
                
                half4 cornerTex = SAMPLE_TEXTURE2D( _TopMap, sampler_TopMap, tiledUV);
                 
                half4 color;
                wallTex = lerp(downColor,wallTex,(1 && _cur)*wallTex.a);
                wallTex = lerp(wallTex,cornerTex,(1 && _cur_corner)*cornerTex.a);
                color.rgb=wallTex;
                color.a = _alpha;
                return color;
            }
            ENDHLSL
        }
    }
}
