// Presentation-only stage surface: vertex-colour albedo (baked AO included), optional
// atlas multiply, triplanar detail grit and gentle cloth/foliage wind. Vertex alpha
// masks both effects: detail strength and wind weight. Gameplay never reads this geometry.
Shader "SLOPArena/StageVertexLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Atlas", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _DetailMap("Detail (triplanar)", 2D) = "gray" {}
        _DetailScale("Detail Scale (1/m)", Float) = 0.4
        _DetailStrength("Detail Strength", Range(0, 1)) = 0.3
        _ShadowTint("Shadow Tint", Color) = (0.62, 0.68, 0.82, 1)
        _Wrap("Light Wrap", Range(0, 1)) = 0.2
        _Specular("Specular", Range(0, 2)) = 0
        _Gloss("Gloss", Range(4, 128)) = 24
        _Emission("Emission", Range(0, 8)) = 0
        _Wind("Wind Strength (m)", Range(0, 1)) = 0
        _WindFrequency("Wind Frequency", Float) = 1.7
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.35, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [Toggle(_RECEIVE_SHADOWS_OFF)] _ReceiveShadowsOff("Receive Shadows Off", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "SlopArenaStageCutaway.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float4 _DetailMap_ST;
            float _DetailScale;
            half _DetailStrength;
            half4 _ShadowTint;
            half _Wrap;
            half _Specular;
            half _Gloss;
            half _Emission;
            half _Wind;
            float _WindFrequency;
            float4 _WindDirection;
            half _Cull;
            half _ReceiveShadowsOff;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);

        float3 StageWind(float3 positionWS, half weight)
        {
            float t = _Time.y * _WindFrequency;
            float phase = dot(positionWS, float3(0.37, 0.21, 0.29));
            float sway = sin(t + phase) * 0.65 + sin(t * 2.3 + phase * 1.7) * 0.35;
            float3 dir = normalize(float3(_WindDirection.x, 0.0, _WindDirection.z) + float3(1e-4, 0.0, 0.0));
            float3 flutter = float3(0.0, 0.3 * sin(t * 3.1 + phase * 2.3), 0.0);
            return (dir * sway + flutter) * (_Wind * weight);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += StageWind(positionWS, input.color.a);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
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

            half3 Diffuse(Light light, half3 normalWS, half shadowed)
            {
                half ndl = dot(normalWS, light.direction);
                half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                return light.color * (wrapped * light.distanceAttenuation * shadowed);
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                StageCutawayClip(input.positionWS, input.positionCS);
                half3 normalWS = normalize(input.normalWS);
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);

                half3 albedo = input.color.rgb * _BaseColor.rgb
                    * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 blend = pow(abs(normalWS), 4.0h);
                blend /= max(1e-4h, blend.x + blend.y + blend.z);
                float3 p = input.positionWS * _DetailScale;
                half detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.zy).r * blend.x
                    + SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.xz).r * blend.y
                    + SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.xy).r * blend.z;
                albedo *= lerp(1.0h, detail * 2.0h, _DetailStrength * input.color.a);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadowed = mainLight.shadowAttenuation;
                #if defined(_RECEIVE_SHADOWS_OFF)
                shadowed = 1.0h;
                #endif
                half3 direct = Diffuse(mainLight, normalWS, shadowed);
                half lit = saturate(dot(direct, half3(0.333h, 0.333h, 0.334h)) / max(0.05h, dot(mainLight.color, half3(0.333h, 0.333h, 0.334h))));
                half3 ambient = max(SampleSH(normalWS), half3(0, 0, 0)) * lerp(_ShadowTint.rgb, half3(1, 1, 1), lit);

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    direct += Diffuse(light, normalWS, light.shadowAttenuation);
                }
                #endif

                half3 color = albedo * (ambient + direct);
                if (_Specular > 0.0h)
                {
                    half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                    half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                    half highlight = pow(saturate(dot(normalWS, halfDir)), _Gloss) * _Specular * shadowed;
                    half rim = pow(1.0h - saturate(dot(normalWS, viewDir)), 3.0h) * 0.35h * _Specular;
                    color += mainLight.color * albedo * (highlight * 2.0h) + albedo * ambient * rim;
                }
                color += albedo * _Emission;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += StageWind(positionWS, input.color.a);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += StageWind(positionWS, input.color.a);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                StageCutawayClip(input.positionWS, input.positionCS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing

            struct NormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            NormalsVaryings NormalsVert(NormalsAttributes input)
            {
                NormalsVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += StageWind(positionWS, input.color.a);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(NormalsVaryings input) : SV_Target
            {
                StageCutawayClip(input.positionWS, input.positionCS);
                return half4(normalize(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
