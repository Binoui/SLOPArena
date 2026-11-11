// A soft shaft of light in the air below a stage lamp: an additive cone built by StageLook, with vertex alpha
// 1 at the lamp and 0 at the floor. Presentation only.
Shader "SLOPArena/Stage Light Shaft"
{
    Properties { _Color("Colour (a = strength)", Color) = (1, 0.9, 0.7, 0.14) }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Shaft"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half4 color : COLOR; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = abs(dot(normalize(input.normalWS), viewDir));
                half core = smoothstep(0.05h, 0.85h, facing);          // soft silhouette, denser through the middle
                half along = saturate(input.color.a * 2.2h) * (0.45h + 0.55h * input.color.a);
                return half4(_Color.rgb * (_Color.a * core * along), 0);
            }
            ENDHLSL
        }
    }
}
