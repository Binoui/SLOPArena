Shader "SLOPArena/Homing Dash Outline"
{
    // Inverted-hull lock-on outline drawn by a skin copy while the homing dash flies (ADR-0042).
    Properties
    {
        _Color("Outline", Color) = (1, 0.8, 0.133, 1)
        _Width("Width (m)", Float) = 0.035
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                world += normalize(TransformObjectToWorldNormal(input.normalOS)) * _Width;
                output.positionCS = TransformWorldToHClip(world);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
