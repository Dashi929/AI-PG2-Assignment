Shader "Unlit/DayNightShader"
{
    Properties
    {
        _DayTex ("DayTex", CUBE) = "white" {}
        _NightTex ("NightTex", CUBE) = "white" {}
        _Blend ("Blend", Range(0,1)) = 0.5
        _Rotation ("Rotation", Range(0,360)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 uv : TEXCOORD0;
            };

            struct v2f
            {
                float3 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            samplerCUBE _DayTex;
            samplerCUBE _NightTex;
            float _Blend;
            float _Rotation;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // rotate the texture sampling direction around the X axis (degrees to radians)
                float rad = _Rotation * UNITY_PI / 180.0;
                float c = cos(rad);
                float s = sin(rad);
                float3 dir = i.uv;
                dir.yz = float2(dir.y * c - dir.z * s, dir.y * s + dir.z * c);

                fixed4 dayColor = texCUBE(_DayTex, dir);
                fixed4 nightColor = texCUBE(_NightTex, dir);
                fixed4 finalColor = lerp(nightColor, dayColor, _Blend);
                return finalColor;
            }
            ENDCG
        }
    }
}