// ProceduralTerrain — procedural water shader (URP)
// Features:
//  - Layered sine-wave displacement per vertex (3 octaves, different direction/speed)
//  - Analytic normal perturbation for specular highlights
//  - Vertex colour R channel drives shallow/deep water blending (depth baked by WaterSurface.cs)
//  - Translucent + URP main-light diffuse and specular
// Requires the Universal RP package; falls back to an opaque blue-green Standard shader.

Shader "ProceduralTerrain/WaterURP"
{
    Properties
    {
        _Color ("Shallow Color", Color) = (0.10, 0.55, 0.62, 0.82)
        _DeepColor ("Deep Color", Color) = (0.03, 0.18, 0.32, 0.95)
        _WaveAmp ("Wave Amplitude", Float) = 0.12
        _WaveFreq ("Wave Frequency", Float) = 1.6
        _WaveSpeed ("Wave Speed", Float) = 0.55
        _Glossiness ("Glossiness", Range(0, 1)) = 0.45
        _SpecPower ("Spec Power", Range(8, 200)) = 48
        _WaterLevel ("Water Level (world Y)", Float) = 30.0
        _WaveStep ("Wave Step (m, low-poly)", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _DeepColor;
                half _WaveAmp;
                half _WaveFreq;
                half _WaveSpeed;
                half _Glossiness;
                half _SpecPower;
                half _WaterLevel;
                half _WaveStep;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 vertexColor : COLOR0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float depth01 : TEXCOORD2;
                float shoreAlpha : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            // sum three sine waves with different directions/periods; returns height offset
            float WaveHeight(float2 pos)
            {
                float t = _Time.y * _WaveSpeed;
                float w1 = sin(pos.x * _WaveFreq + t);
                float w2 = sin(pos.y * _WaveFreq * 1.31 + t * 1.17);
                float w3 = sin((pos.x + pos.y) * _WaveFreq * 0.53 + t * 0.63);
                return _WaveAmp * (0.55 * w1 + 0.30 * w2 + 0.15 * w3);
            }

            // analytic normal from partial derivatives of the wave function
            float3 WaveNormal(float2 pos)
            {
                float t = _Time.y * _WaveSpeed;
                float2 d = _WaveAmp * float2(
                    0.55 * _WaveFreq * cos(pos.x * _WaveFreq + t)
                        + 0.15 * _WaveFreq * 0.53 * cos((pos.x + pos.y) * _WaveFreq * 0.53 + t * 0.63),
                    0.30 * _WaveFreq * 1.31 * cos(pos.y * _WaveFreq * 1.31 + t * 1.17)
                        + 0.15 * _WaveFreq * 0.53 * cos((pos.x + pos.y) * _WaveFreq * 0.53 + t * 0.63));
                return normalize(float3(-d.x, 1.0, -d.y));
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float2 p = input.positionOS.xz;
                float wave = WaveHeight(p);
                // 低多边形：波浪位移量化成台阶（块状波面）
                float step = max(_WaveStep, 1e-4);
                wave = floor(wave / step) * step;

                float3 posOS = float3(p.x, input.positionOS.y + wave, p.y);
                float3 posWS = TransformObjectToWorld(posOS);

                o.positionCS = TransformWorldToHClip(posWS);
                o.worldPos = posWS;
                o.normalWS = WaveNormal(p);
                o.depth01 = saturate(input.vertexColor.r);
                o.shoreAlpha = saturate(input.vertexColor.a);   // 岸边透明度（0=陆地，1=深水）
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 陆地像素（水深 0）：直接丢弃，水面只显示湖泊区域（像素级裁剪，无几何锯齿）
                if (i.depth01 < 0.001) discard;

                // 平坦着色：面法线（低多边形明暗块），退化时回退解析法线
                float3 g = cross(ddy(i.worldPos), ddx(i.worldPos));
                float3 N = length(g) > 1e-5 ? normalize(g) : i.normalWS;
                if (N.y < 0) N = -N;

                // depth gradient: _DeepColor in deep areas, _Color near shore
                half3 waterCol = lerp(_Color.rgb, _DeepColor.rgb, i.depth01);

                // main light diffuse + ambient（随昼夜变化：白天亮、夜里暗）
                Light mainLight = GetMainLight();
                float ndl = saturate(dot(N, mainLight.direction));
                float3 ambient = SampleSH(N);
                half3 col = waterCol * (ndl * mainLight.color * 0.85 + ambient * 0.9);

                // specular highlights on wave crests
                float3 viewDir = normalize(GetCameraPositionWS() - i.worldPos);
                float3 halfVec = normalize(mainLight.direction + viewDir);
                col += pow(saturate(dot(N, halfVec)), _SpecPower)
                     * _Glossiness * mainLight.color;

                half alpha = lerp(_Color.a, _DeepColor.a, i.depth01) * i.shoreAlpha;
                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Standard"
}
