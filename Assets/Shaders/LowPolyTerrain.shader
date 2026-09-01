Shader "ProceduralTerrain/LowPolyTerrain"
{
    Properties
    {
        [HideInInspector] [PerRendererData] _Control("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] [PerRendererData] _Control1("Control1 (RGBA)", 2D) = "red" {}
        [HideInInspector] [PerRendererData] _Splat0("Layer 0 (R)", 2D) = "grey" {}
        [HideInInspector] [PerRendererData] _Splat1("Layer 1 (G)", 2D) = "grey" {}
        [HideInInspector] [PerRendererData] _Splat2("Layer 2 (B)", 2D) = "grey" {}
        [HideInInspector] [PerRendererData] _Splat3("Layer 3 (A)", 2D) = "grey" {}
        [HideInInspector] [PerRendererData] _Splat4("Layer 4 (R)", 2D) = "grey" {}
        [HideInInspector] [PerRendererData] _Splat5("Layer 5 (G)", 2D) = "grey" {}
        _HeightStep("高度台阶 (m)", Range(0.5, 8)) = 3.0
        _SunPower("阳光强度", Range(0, 2)) = 1.0
        _AmbientBoost("环境光", Range(0, 1)) = 0.6
        _Saturation("饱和度", Range(0, 2)) = 1.35
        _Brightness("亮度", Range(0, 2)) = 1.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "TerrainCompatible" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Splat0_ST;
                float4 _Splat1_ST;
                float4 _Splat2_ST;
                float4 _Splat3_ST;
                float4 _Splat4_ST;
                float4 _Splat5_ST;
                float _HeightStep;
                float _SunPower;
                float _AmbientBoost;
                float _Saturation;
                float _Brightness;
            CBUFFER_END

            TEXTURE2D(_Control);   SAMPLER(sampler_Control);
            TEXTURE2D(_Control1);  SAMPLER(sampler_Control1);
            TEXTURE2D(_Splat0);    SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1);
            TEXTURE2D(_Splat2);
            TEXTURE2D(_Splat3);
            TEXTURE2D(_Splat4);
            TEXTURE2D(_Splat5);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float  fogFactor   : TEXCOORD2;
                float4 clipPos     : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                // 低多边形化：高度台阶化（顶点 xz 保持，避免三角形退化）
                positionWS.y = floor(positionWS.y / _HeightStep) * _HeightStep;

                OUT.positionWS = positionWS;
                OUT.uv = IN.texcoord;
                OUT.clipPos = TransformWorldToHClip(positionWS);
                OUT.fogFactor = ComputeFogFactor(OUT.clipPos.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // 平坦着色：用屏幕空间梯度求面法线，并确保朝上
                float3 n = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));
                if (n.y < 0) n = -n;
                // 退化保护：梯度太小（共面像素）时回退到顶点法线
                if (length(cross(ddy(IN.positionWS), ddx(IN.positionWS))) < 1e-5)
                    n = float3(0, 1, 0);

                // 6 层 splatmap 混合（第 2 张控制图存 沙/道路）
                half4 c0 = SAMPLE_TEXTURE2D(_Control, sampler_Control, IN.uv);
                half4 c1 = SAMPLE_TEXTURE2D(_Control1, sampler_Control1, IN.uv);
                half wSand = c1.r;
                half wRoad = c1.g;
                half wSum = c0.r + c0.g + c0.b + c0.a + wSand + wRoad;
                if (wSum > 1e-4)
                {
                    c0 /= wSum;
                    wSand /= wSum;
                    wRoad /= wSum;
                }

                half3 col =
                    c0.r * SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, TRANSFORM_TEX(IN.uv, _Splat0)).rgb +
                    c0.g * SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, TRANSFORM_TEX(IN.uv, _Splat1)).rgb +
                    c0.b * SAMPLE_TEXTURE2D(_Splat2, sampler_Splat0, TRANSFORM_TEX(IN.uv, _Splat2)).rgb +
                    c0.a * SAMPLE_TEXTURE2D(_Splat3, sampler_Splat0, TRANSFORM_TEX(IN.uv, _Splat3)).rgb +
                    wSand * SAMPLE_TEXTURE2D(_Splat4, sampler_Splat0, TRANSFORM_TEX(IN.uv, _Splat4)).rgb +
                    wRoad * SAMPLE_TEXTURE2D(_Splat5, sampler_Splat0, TRANSFORM_TEX(IN.uv, _Splat5)).rgb;

                Light mainLight = GetMainLight();
                half ndl = saturate(dot(n, mainLight.direction));
                half3 ambient = SampleSH(n);
                half3 final = col * (mainLight.color * ndl * _SunPower + ambient * _AmbientBoost);
                // 鲜艳度：饱和度 + 亮度增强
                half luma = dot(final, half3(0.299, 0.587, 0.114));
                final = lerp(luma.xxx, final, _Saturation) * _Brightness;
                final = MixFog(final, IN.fogFactor);
                return half4(final, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Terrain/Lit"
}
