Shader "SLOPArena/Particles/CFXR3 Fire Explosion"
{
    Properties
    {
        [MainTexture] _MainTex("Particle Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        [HDR] _HdrMultiply("HDR Multiplier", Float) = 1
        [Toggle] _SingleChannel("Single Channel Texture", Float) = 0
        _DissolveTex("Dissolve Texture", 2D) = "white" {}
        _DistortTex("Distortion Texture", 2D) = "gray" {}
        _Distort("Distortion Strength", Range(0, 2)) = 0
        _DissolveSmooth("Dissolve Smoothness", Range(0.001, 1)) = 0.05
        [Toggle] _UseDissolve("Use Dissolve", Float) = 0
        [Toggle] _InvertDissolveTex("Invert Dissolve", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination Blend", Float) = 1
        [Toggle] _ZWrite("Depth Write", Float) = 0
        [Toggle(_SOFT_PARTICLES_ON)] _UseSoftParticles("Soft Particles", Float) = 1
        _SoftParticlesFadeDistance("Soft Particle Fade Distance", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _SOFT_PARTICLES_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_DissolveTex); SAMPLER(sampler_DissolveTex);
            TEXTURE2D(_DistortTex); SAMPLER(sampler_DistortTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _DissolveTex_ST;
                float4 _DistortTex_ST;
                half4 _BaseColor;
                half _HdrMultiply;
                half _SingleChannel;
                half _Distort;
                half _DissolveSmooth;
                half _UseDissolve;
                half _InvertDissolveTex;
                half _SoftParticlesFadeDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 texcoord : TEXCOORD0;
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
                float2 uv = input.uv;
                half distortion = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex,
                    TRANSFORM_TEX(uv, _DistortTex)).r * 2.0h - 1.0h;
                uv += distortion * _Distort;

                half4 particle = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                if (_SingleChannel > 0.5h)
                    particle = half4(1, 1, 1, particle.r);

                half3 color = particle.rgb * input.color.rgb * _HdrMultiply;
                half alpha = particle.a * input.color.a;

                if (_UseDissolve > 0.5h)
                {
                    half dissolve = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex,
                        TRANSFORM_TEX(uv, _DissolveTex)).r;
                    if (_InvertDissolveTex > 0.5h)
                        dissolve = 1.0h - dissolve;
                    alpha *= smoothstep(0.0h, max(_DissolveSmooth, 0.001h), dissolve);
                }

                #if defined(_SOFT_PARTICLES_ON)
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float particleDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    alpha *= saturate((sceneDepth - particleDepth)
                        / max(_SoftParticlesFadeDistance, 0.001h));
                #endif

                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
