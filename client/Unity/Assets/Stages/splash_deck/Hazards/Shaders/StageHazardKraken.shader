// Presentation only: the kraken's skinned body. The SLOPArena/StageVertexLit look (vertex-colour
// albedo with baked AO, wrapped sun, tinted ambient, a little specular, stage sight holes) minus
// its atlas, detail and wind, plus one thing the stage never needs: a dithered screen-door fade
// for the parts that stand between the camera and the deck. The fight camera hangs over the
// starboard water, exactly where the starboard kraken surfaces, so its head and spare arms would
// fill the screen. A fragment outside the deck outline whose view ray goes on to hit the deck keeps
// only _OccluderKeep of its pixels, as does the whip higher than _SolidHeight over the deck within
// about 20 m of the camera; anything nearer the camera than _CameraFadeNear vanishes and it is
// solid again beyond _CameraFadeFar. The arm lying on the deck, the far-side kraken and shadows
// are untouched. Unity skins the mesh before the vertex stage, so nothing here is skinning-specific.
Shader "SlopArena/StageHazardKraken"
{
    Properties
    {
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _ShadowTint("Shadow Tint", Color) = (0.62, 0.6, 0.82, 1)
        _Wrap("Light Wrap", Range(0, 1)) = 0.35
        _Specular("Specular", Range(0, 2)) = 0.15
        _Gloss("Gloss", Range(4, 128)) = 22
        _CameraFadeNear("Gone nearer than (m)", Float) = 4
        _CameraFadeFar("Solid beyond (m)", Float) = 11
        _OccluderKeep("Pixels kept where it hides the deck", Range(0, 1)) = 0.2
        _DeckY("Deck top", Float) = 0
        _SolidHeight("Arm stays solid below this height over the deck", Float) = 4.5
        _DeckHalfBeam("Deck half beam", Float) = 11
        _DeckMinX("Deck stern x", Float) = -15.5
        _DeckMaxX("Deck bow x", Float) = 20.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/SlopArenaStageCutaway.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadowTint;
            half _Wrap;
            half _Specular;
            half _Gloss;
            float _CameraFadeNear;
            float _CameraFadeFar;
            half _OccluderKeep;
            float _DeckY;
            float _SolidHeight;
            float _DeckHalfBeam;
            float _DeckMinX;
            float _DeckMaxX;
        CBUFFER_END

        // Screen-door fade: interleaved gradient noise against the distance ramp.
        void CameraFadeClip(float3 positionWS, float4 positionCS)
        {
            float3 camera = _WorldSpaceCameraPos;
            float keep = 1.0;
            if (_CameraFadeFar > _CameraFadeNear)
                keep = saturate((distance(positionWS, camera) - _CameraFadeNear) / (_CameraFadeFar - _CameraFadeNear));
            float3 ray = positionWS - camera;
            if (ray.y < -1e-3)
            {
                float t = (_DeckY - camera.y) / ray.y;
                float3 hit = camera + ray * t;
                bool deckBehind = t > 1.0 && abs(hit.z) < _DeckHalfBeam + 0.5 && hit.x > _DeckMinX - 0.5 && hit.x < _DeckMaxX + 0.5;
                float outside = max(abs(positionWS.z) - (_DeckHalfBeam + 2.0), max(_DeckMinX - 2.0 - positionWS.x, positionWS.x - _DeckMaxX - 2.0));
                // The whip high over the deck near the camera hides the lane; the low warning never fades.
                float high = smoothstep(_SolidHeight, _SolidHeight + 2.0, positionWS.y - _DeckY)
                    * (1.0 - smoothstep(16.0, 22.0, length(ray)));
                if (deckBehind) keep *= lerp(1.0, _OccluderKeep, max(smoothstep(0.0, 2.5, outside), high));
            }
            float noise = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
            clip(keep - noise - 0.001);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                half4 color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                color.rgb = SRGBToLinear(color.rgb);
                #endif
                output.color = color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half3 Diffuse(Light light, half3 normalWS, half shadowed)
            {
                half ndl = dot(normalWS, light.direction);
                half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                return light.color * (wrapped * light.distanceAttenuation * shadowed);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                CameraFadeClip(input.positionWS, input.positionCS);
                half4 sightRim = StageCutawayClipRim(input.positionWS, input.positionCS);
                half3 normalWS = normalize(input.normalWS);
                half3 albedo = input.color.rgb * _BaseColor.rgb;

                // With a position, so shadows fade at the quality level's shadow distance (see StageVertexLit).
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, half4(1, 1, 1, 1));
                half shadowed = mainLight.shadowAttenuation;
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
                color = MixFog(color, input.fogFactor);
                return half4(StageCutawayApplyRim(color, sightRim), 1.0h);
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthAttributes { float4 positionOS : POSITION; };
            struct DepthVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                CameraFadeClip(input.positionWS, input.positionCS);
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

            struct NormalsAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct NormalsVaryings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            NormalsVaryings NormalsVert(NormalsAttributes input)
            {
                NormalsVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(NormalsVaryings input) : SV_Target
            {
                CameraFadeClip(input.positionWS, input.positionCS);
                StageCutawayClip(input.positionWS, input.positionCS);
                return half4(normalize(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
