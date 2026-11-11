// A soft dark disc on the floor under a fighter: it grounds fighters on stages without real-time shadows and
// firms the contact on stages that have them. TargetIndicator places it with the ground marker.
Shader "SLOPArena/Fighter Contact Shadow"
{
    Properties { _Color("Colour (a = strength)", Color) = (0.05, 0.05, 0.08, 0.3) }
    SubShader
    {
        // Before the ground marker and every other transparent effect, so they draw over it.
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half d = saturate(1.0h - length(input.uv - 0.5h) * 2.0h);
                return half4(_Color.rgb, _Color.a * smoothstep(0.0h, 0.75h, d));
            }
            ENDHLSL
        }
    }
}
