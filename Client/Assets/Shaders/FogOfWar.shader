Shader "Hidden/Station/FogOfWar"
{
    // Полноэкранный туман войны для URP 17 (Full Screen Pass Renderer Feature).
    // По мировой XZ-позиции каждого пикселя сэмплит поле видимости (_FovTex,
    // заполняет FovRenderer) и затемняет невидимые зоны. Затемняет ВСЁ: пол, стены,
    // сущности — потому что работает по реконструированной из глубины мировой точке.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "FogOfWar"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Vert/Varyings/_BlitTexture/sampler_LinearClamp (в URP 17 Blit.hlsl лежит в core):
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            // SampleSceneDepth:
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FovTex);
            SAMPLER(sampler_FovTex);
            float4 _FovParams;        // xy = мировой XZ угла поля, zw = 1/размер_в_тайлах
            float _FovMinBrightness;  // яркость невидимых зон (0 = чёрные)
            float _FovTexel;          // 1 / размер текстуры поля (один тексель в UV)
            float _FovSoft;           // мягкость края тени в текселях (GPU-сглаживание)

            float SampleFov(float2 uv)
            {
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return 0.0;
                return SAMPLE_TEXTURE2D(_FovTex, sampler_FovTex, uv).r;
            }

            half4 Frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Реконструируем мировую точку пикселя из глубины.
                float rawDepth = SampleSceneDepth(uv);
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                // Мировые XZ -> UV поля видимости. Вне поля (за радиусом) = темно.
                // Мировые XZ -> UV поля видимости. 3x3 гауссова выборка = мягкий
                // градиент свет↔тень без видимых пикселей (сглаживание на GPU).
                float2 fovUV = (worldPos.xz - _FovParams.xy) * _FovParams.zw;
                float o = _FovTexel * _FovSoft;
                float light =
                    SampleFov(fovUV) * 0.25 +
                    (SampleFov(fovUV + float2(o, 0)) + SampleFov(fovUV + float2(-o, 0)) +
                     SampleFov(fovUV + float2(0, o)) + SampleFov(fovUV + float2(0, -o))) * 0.125 +
                    (SampleFov(fovUV + float2(o, o)) + SampleFov(fovUV + float2(-o, o)) +
                     SampleFov(fovUV + float2(o, -o)) + SampleFov(fovUV + float2(-o, -o))) * 0.0625;

                float bright = lerp(_FovMinBrightness, 1.0, saturate(light));
                color.rgb *= bright;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
