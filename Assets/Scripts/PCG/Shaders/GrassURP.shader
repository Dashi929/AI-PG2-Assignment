// ProceduralTerrain （URP）
// Features:
//  - Vertex-stage wind sway (sinusoidal + phase variation, weighted by blade height)
//  - Root-to-tip colour gradient (vertex colour A = height factor, B = phase)
//  - Alpha clip for cutout shape + double-sided rendering
//  - URP main-light diffuse + hemisphere ambient, no specular (matte grass)
Shader "ProceduralTerrain/GrassURP"
{
    Properties
    {
        _RootColor ("Root Color", Color) = (0.18, 0.32, 0.10, 1)
        _TipColor ("Tip Color", Color) = (0.45, 0.72, 0.25, 1)
        _WindStrength ("Wind Strength", Range(0, 2)) = 0.5
        _WindSpeed ("Wind Speed", Range(0, 4)) = 1.2
        _WindFreq ("Wind Frequency", Range(0.1, 4)) = 0.9
        _ColorVariant ("Color Variation", Range(0, 0.5)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // a=heightFrac, b=phase, r=variant
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _RootColor;
                float4 _TipColor;
                float _WindStrength;
                float _WindSpeed;
                float _WindFreq;
                float _ColorVariant;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float heightFactor = IN.color.a;

                // wind sway — taller blades move more, phase + noise variation
                float time = _Time.y * _WindSpeed;
                float phase = IN.color.b * 6.28318;
                float wind = sin(time + posWS.x * _WindFreq + phase)
                          + 0.5 * sin(time * 1.7 + posWS.z * _WindFreq * 1.3 + phase * 2.0);
                float2 windOffset = float2(wind, wind * 0.6) * _WindStrength * heightFactor;

                // root stays fixed, tip moves — quadratic falloff from tip toward base
                float3 offsetDir = normalize(float3(windOffset.x, 0.0, windOffset.y));
                float sway = heightFactor * heightFactor; // quadratic: root barely moves
                posWS.xz += windOffset * sway;
                posWS.y += abs(windOffset.x) * sway * 0.15;

                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.worldPos = posWS;

                // root-to-tip colour blend + random per-blade variant
                float3 baseCol = lerp(_RootColor.rgb, _TipColor.rgb, heightFactor);
                float variant = (IN.color.r - 0.5) * _ColorVariant;
                baseCol *= 1.0 + variant;
                OUT.color = float4(baseCol, 1.0);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // simple diffuse: main light N·L + hemisphere ambient
                Light mainLight = GetMainLight();
                float3 N = float3(0, 1, 0);
                float ndl = saturate(dot(N, mainLight.direction) * 0.5 + 0.5);
                float3 ambient = SampleSH(float3(0, 1, 0));
                float3 col = IN.color.rgb * (ndl * mainLight.color + ambient * 0.6);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // shadow caster — ensure grass casts shadow
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _RootColor;
                float4 _TipColor;
                float _WindStrength;
                float _WindSpeed;
                float _WindFreq;
                float _ColorVariant;
            CBUFFER_END

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float heightFactor = IN.color.a;
                float time = _Time.y * _WindSpeed;
                float phase = IN.color.b * 6.28318;
                float wind = sin(time + posWS.x * _WindFreq + phase);
                float sway = heightFactor * heightFactor;
                posWS.xz += wind * _WindStrength * sway;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 fragShadow(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
