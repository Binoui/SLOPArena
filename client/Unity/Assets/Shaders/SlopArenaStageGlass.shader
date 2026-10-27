// Presentation-only clear plastic for stage parts fighters are seen through (hamster tubes,
// a wheel). Vertex colour tints, _BaseColor.a sets the face-on opacity and the rim makes
// grazing angles read as solid. Transparent queue, no depth write, no shadows.
Shader "SLOPArena/StageGlass"
{
    Properties
    {
        [MainTexture] _BaseMap("Atlas", 2D) = "white" {}
        [MainColor] _BaseColor("Tint (alpha = face-on opacity)", Color) = (0.8, 0.92, 1, 0.12)
        _Rim("Rim Opacity", Range(0, 1)) = 0.45
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5
        _Specular("Sheen", Range(0, 2)) = 0.6
        _Gloss("Sheen Tightness", Range(4, 256)) = 64
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Rim;
                half _RimPower;
                half _Specular;
                half _Gloss;
                half _Cull;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                half4 color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                color.rgb = SRGBToLinear(color.rgb);
                #endif
                output.color = color;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 tint = input.color.rgb * _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;

                Light mainLight = GetMainLight();
                half ndl = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h);
                half3 ambient = max(SampleSH(normalWS), half3(0, 0, 0));
                half3 color = tint * (ambient + mainLight.color * ndl);
                half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                half sheen = pow(saturate(dot(normalWS, halfDir)), _Gloss) * _Specular;
                color += mainLight.color * sheen;

                half rim = pow(1.0h - saturate(dot(normalWS, viewDir)), _RimPower) * _Rim;
                half alpha = saturate(_BaseColor.a + rim + sheen * 0.5h);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
