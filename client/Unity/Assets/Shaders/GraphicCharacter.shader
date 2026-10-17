Shader "SLOPArena/GraphicCharacter"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _ShadowColor("Shadow Tint", Color) = (0.20, 0.24, 0.34, 1)
        _RimColor("Rim Color", Color) = (1.0, 0.72, 0.20, 1)
        _RimPower("Rim Power", Range(1, 8)) = 3
        _RimStrength("Rim Strength", Range(0, 2)) = 0.65
        // Hit flash, driven per renderer by FighterHitFeel through a MaterialPropertyBlock.
        _FlashColor("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount("Flash Amount", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                half4 _FlashColor;
                half _FlashAmount;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half wrappedLight = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h);
                // Broad, soft steps retain the graphic look without cutting across faces.
                half band = 0.35h + 0.40h * smoothstep(0.30h, 0.58h, wrappedLight)
                    + 0.25h * smoothstep(0.65h, 0.88h, wrappedLight);
                half3 diffuseTint = lerp(_ShadowColor.rgb, half3(1, 1, 1), band);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half shadowAttenuation = lerp(0.45h, 1.0h, mainLight.shadowAttenuation);
                half3 ambient = max(SampleSH(normalWS), half3(0, 0, 0));
                half3 color = albedo.rgb * (ambient
                    + diffuseTint * mainLight.color * shadowAttenuation);

                // A restrained satin highlight gives volume without a wet/plastic finish.
                half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                half highlight = pow(saturate(dot(normalWS, halfDir)), 32.0h)
                    * smoothstep(0.45h, 0.75h, wrappedLight);
                color += mainLight.color * highlight * 0.16h * mainLight.shadowAttenuation;

                // Keep player colour on the silhouette, not across the entire material.
                half rim = pow(saturate(1.0h - dot(normalWS, viewDir)), _RimPower + 1.5h)
                    * _RimStrength * 0.4h;
                color += _RimColor.rgb * rim;
                color = lerp(color, _FlashColor.rgb, _FlashAmount);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
