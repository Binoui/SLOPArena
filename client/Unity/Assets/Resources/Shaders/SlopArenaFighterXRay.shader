Shader "SLOPArena/FighterXRay"
{
    // Seat-colour silhouette of the parts of a fighter hidden by the stage. Queue 2040 runs after the
    // stage (2000) and before fighters moved to 2050, so ZTest Greater sees stage depth only.
    Properties
    {
        _Color ("Seat Colour", Color) = (1, 0.8, 0.2, 1)
        _Fill ("Fill Opacity", Range(0, 1)) = 0.35
        _Edge ("Edge Opacity", Range(0, 1)) = 0.95
        _EdgePower ("Edge Power", Range(0.5, 8)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+40" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "XRay"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fill;
                half _Edge;
                half _EdgePower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half edge = pow(1.0h - facing, _EdgePower);
                half alpha = lerp(_Fill, _Edge, edge);
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
