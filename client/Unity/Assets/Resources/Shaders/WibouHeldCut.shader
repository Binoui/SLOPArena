Shader "SLOPArena/Wibou Held Cut"
{
    Properties
    {
        _Color("Colour", Color) = (0.57, 1, 0.41, 1)
        _Width("Outline width", Float) = 0.022
        _Mode("Mode", Float) = 0
        _Cull("Cull", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float _Mode;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                if (_Mode < 1.5) output.world += normalize(TransformObjectToWorldNormal(input.normalOS)) * _Width;
                output.positionCS = TransformWorldToHClip(output.world);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_Mode > 1.5)
                {
                    float d = max(abs(input.uv.x - 0.5), abs(input.uv.y - 0.5));
                    half3 c = d > 0.34 ? half3(0.031, 0.188, 0.071) : d > 0.19 ? half3(0.569, 1, 0.412) : half3(1, 1, 1);
                    return half4(c, input.color.a);
                }
                if (_Mode > 0.5) clip(0.42 - frac(input.world.y * 42));
                return _Color;
            }
            ENDHLSL
        }
    }
}
