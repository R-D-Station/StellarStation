Shader "Station/TileFaceSprites"
{
    // URP UNLIT для 3D-мешей тайлов: БОКОВЫЕ грани текстурятся одним спрайтом (_SideTex),
    // ВЕРХ — отдельным (_TopTex). Текстура верх/бок выбирается по МИРОВОЙ нормали (|n.y|>0.5 = верх),
    // оси UV — по доминантной ОБЪЕКТНОЙ нормали, нормировка позиции по габаритам меша (_BoundsMin/_BoundsSize).
    // Модель освещения проекта — «свет = FOV»: тайл выводится на ПОЛНУЮ яркость спрайта, а видимость/затемнение
    // даёт full-screen FOV-туман (реконструирует мир из глубины) — поэтому DepthOnly обязателен. PBR/ламп нет.
    // Все свойства MPB-overridable: MapRenderer выставляет per-инстанс текстуры/габариты (выводит из SRP Batcher — ок).
    Properties
    {
        [MainColor] _BaseColor("Base Color (тинт)", Color) = (1,1,1,1)
        [MainTexture] _SideTex("Side Texture (боковые грани)", 2D) = "white" {}
        _TopTex("Top Texture (верхняя грань)", 2D) = "white" {}
        _SideST("Side UV ST (xy=scale, zw=offset)", Vector) = (1,1,0,0)
        _TopST("Top UV ST (xy=scale, zw=offset)", Vector) = (1,1,0,0)
        _BoundsMin("Mesh Bounds Min (object, xyz)", Vector) = (-0.5,-0.5,-0.5,0)
        _BoundsSize("Mesh Bounds Size (object, xyz)", Vector) = (1,1,1,0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // Общие объявления для всех проходов: CBUFFER UnityPerMaterial (SRP-Batcher-совместим)
        // + сэмплеры. Текстуры — вне CBUFFER (по правилам HLSL), MPB.SetTexture их переопределяет.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _SideST;
            float4 _TopST;
            float4 _BoundsMin;   // габариты меша в объектном пространстве (xyz) — нормировка UV граней
            float4 _BoundsSize;
        CBUFFER_END

        TEXTURE2D(_SideTex); SAMPLER(sampler_SideTex);
        TEXTURE2D(_TopTex);  SAMPLER(sampler_TopTex);
        ENDHLSL

        // Основной проход — UNLIT: спрайт грани на полную яркость; затемнение даёт full-screen FOV-туман.
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0; // выбор текстуры верх/бок (мировой верх = +Y)
                half   fogCoord   : TEXCOORD1;
                float3 positionOS : TEXCOORD2; // выбор осей UV + нормировка по габаритам (вращается с мешем)
                float3 normalOS   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs posn = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm  = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posn.positionCS;
                OUT.normalWS   = nrm.normalWS;
                OUT.fogCoord   = ComputeFogFactor(posn.positionCS.z);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS   = IN.normalOS;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 nWS = normalize(IN.normalWS); // выбор текстуры верх/бок (мировой верх = +Y)
                float3 nOS = normalize(IN.normalOS); // выбор осей UV (плоскость грани меша)

                // Нормировка локальной позиции по габаритам меша (_BoundsMin/_BoundsSize) → ровно ОДИН
                // растянутый спрайт на грань, без frac-тайла/зума на не-единичных мешах.
                float3 uv01 = (IN.positionOS - _BoundsMin.xyz) / max(_BoundsSize.xyz, 1e-4); // 0..1 по габаритам

                // ТЕКСТУРА верх/бок — по МИРОВОЙ нормали (мировой верх = +Y), не зависит от того, как у меша
                // развёрнута локальная up-ось (autotiling крутит меш → object-нормаль ≠ +Y давала верх↔бок).
                bool isTop = abs(nWS.y) > 0.5;

                // ОСИ UV — по доминантной ОБЪЕКТНОЙ нормали (плоскость грани): спрайт вращается с мешем, а
                // раскладка xz/zy/xy и «идеальные» размеры остаются прежними.
                float3 an = abs(nOS);
                float2 uvBase;
                if (an.y >= an.x && an.y >= an.z)
                    uvBase = uv01.xz;                 // грань по object-Y: footprint меша
                else if (an.x >= an.z)
                    uvBase = float2(uv01.z, uv01.y);  // грань ±X: гориз=Z, верт=Y
                else
                    uvBase = float2(uv01.x, uv01.y);  // грань ±Z: гориз=X, верт=Y

                float4 st = isTop ? _TopST : _SideST;
                float2 uv = uvBase * st.xy + st.zw;                          // атлас-remap спрайта поверх
                half4 tex = isTop
                    ? SAMPLE_TEXTURE2D(_TopTex,  sampler_TopTex,  uv)
                    : SAMPLE_TEXTURE2D(_SideTex, sampler_SideTex, uv);

                // UNLIT: полное альбедо спрайта (модель «свет = FOV» — яркость гасит full-screen FOV-туман,
                // не сценические лампы). Сценический distance-fog оставлен для паритета с прочими объектами.
                half4 c = tex * _BaseColor;
                c.rgb = MixFog(c.rgb, IN.fogCoord);
                c.a = 1.0; // opaque
                return c;
            }
            ENDHLSL
        }

        // Depth prepass / depth-текстура URP — нужна full-screen FOV-туману (реконструкция мира из глубины).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct AttributesD
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsD
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsD DepthVert(AttributesD input)
            {
                VaryingsD output = (VaryingsD)0;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(VaryingsD input) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
