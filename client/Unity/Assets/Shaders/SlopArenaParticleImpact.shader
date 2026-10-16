Shader "SLOPArena/Particles/Impact"
{
    Properties
    {
        [MainTexture] _MainTex("Particle Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        [Toggle] _SingleChannel("Single Channel Texture", Float) = 0
        [HDR] _HdrMultiplier("HDR Multiplier", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination Blend", Float) = 1
        [Toggle] _ZWrite("Depth Write", Float) = 0
        [Toggle(_ALPHATEST_ON)] _UseAlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Clip Threshold", Range(0,1)) = 0.1
        [Toggle(_SOFT_PARTICLES_ON)] _UseSoftParticles("Soft Particles", Float) = 0
        _SoftParticleFadeDistance("Soft Particle Fade Distance", Range(0.001, 10)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SOFT_PARTICLES_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                half _SingleChannel;
                half _HdrMultiplier;
                half _Cutoff;
                half _SoftParticleFadeDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformWorldToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.texcoord.xy, _MainTex);
                output.color = input.color * _BaseColor;
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 particle = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                if (_SingleChannel > 0.5)
                    particle = half4(1, 1, 1, particle.r);

                half3 color = particle.rgb * input.color.rgb * _HdrMultiplier;
                half alpha = particle.a * input.color.a;

                #if defined(_SOFT_PARTICLES_ON)
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float particleDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    alpha *= saturate((sceneDepth - particleDepth) / max(_SoftParticleFadeDistance, 0.001));
                #endif

                #if defined(_ALPHATEST_ON)
                    clip(alpha - _Cutoff);
                #endif

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
