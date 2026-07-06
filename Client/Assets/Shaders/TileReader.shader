Shader "Custom/TileReader"
{
    Properties
    {
        [MainTexture] _TopMap("Base Map", 2D) = "white" {}
        _cur("_cur",Float)=0
        _cur_corner("_cur_corner",Float)=0
        _rotate("_rotate",Float)=0
        _count("_count",Float)=0


    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_TopMap);
            SAMPLER(sampler_TopMap);

             CBUFFER_START(UnityPerMaterial)
                float4 _TopMap_ST;
                uint _cur;
                uint _cur_corner;
                uint _rotate;
                uint _count;
                
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

                tiledUV.x=(tiledUV.x+_cur-1)/_count;
                tiledUV.y=(tiledUV.y+1)/2;  
                half4 wallTex = SAMPLE_TEXTURE2D( _TopMap, sampler_TopMap, tiledUV);

                tiledUV = rotateUv(IN.uv);
                tiledUV.y=(tiledUV.y)/2;  
                tiledUV.x=(tiledUV.x+_cur_corner-1)/_count;
                
                half4 cornerTex = SAMPLE_TEXTURE2D( _TopMap, sampler_TopMap, tiledUV);
                 
                half4 color;
                half4 downColor = {0,0,0,1};
                wallTex = lerp(downColor,wallTex,(1 && _cur)*wallTex.a);
                wallTex = lerp(wallTex,cornerTex,(1 && _cur_corner)*cornerTex.a);
                color.rgb=wallTex;
                return color;
            }
            ENDHLSL
        }
    }
}
