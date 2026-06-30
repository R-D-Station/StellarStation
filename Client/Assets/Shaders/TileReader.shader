Shader "Custom/TileReader"
{
    Properties
    {
        [MainTexture] _TopMap("Base Map", 2D) = "white" {}
        _i("InputBits",Float)=0
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
                uint _i;
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


            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv,_TopMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                /*uint bits = asuint(_InputBits);
                [unroll]
                for (uint i = 0u;i<32u;i++){
                    uint bit = (bits>>i)&1u;
                }

                uint bit = (bits>>0)&1u;*/
                float2 tiledUV = IN.uv;
                tiledUV.x=(tiledUV.x+_i)/5; 
                half3 tile = SAMPLE_TEXTURE2D( _TopMap, sampler_TopMap, tiledUV);
                half4 color;
                color.rgb=tile;
                return color;
            }
            ENDHLSL
        }
    }
}
