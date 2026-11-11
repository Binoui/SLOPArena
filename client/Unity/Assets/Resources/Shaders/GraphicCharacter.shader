Shader "SLOPArena/GraphicCharacter"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _ShadowColor("Shadow Tint", Color) = (0.20, 0.24, 0.34, 1)
        _RimColor("Rim Color", Color) = (1.0, 0.72, 0.20, 1)
        _RimPower("Rim Power", Range(1, 8)) = 3
        _RimStrength("Rim Strength", Range(0, 2)) = 0.65
        // Hit flash, driven per renderer by FighterHitFeel through a MaterialPropertyBlock.
        _FlashColor("Flash Color", Color) = (1, 1, 1, 1)
        _FlashAmount("Flash Amount", Range(0, 1)) = 0
        _CodeCutHeight("Code dissolve height", Float) = -10000
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+50" // after the stage, so FighterXRay (Geometry+40) sees stage depth only
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/SlopArenaStageLights.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadowColor;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half4 _FlashColor;
            half _FlashAmount;
            float _CodeCutHeight;
        CBUFFER_END

        // The stage's fighter look, set once a match by MatchVisualStyle. Every value is neutral at zero,
        // so a fighter drawn before a match sets them looks as it did without them.
        half _FighterFill;
        half4 _FighterKickColorA;   // rgb colour, a strength
        float4 _FighterKickDirA;    // direction towards the light
        half4 _FighterKickColorB;
        float4 _FighterKickDirB;
        half _FighterKickPower;
        half _FighterLift;          // extra brightness: 0 none, 0.08 = 8% brighter
        half _FighterLampCap;       // most light a fighter takes from the stage's lamps together; 0 = no cap
        half4 _FighterInkColor;
        float4 _FighterInk;         // x width (px, 0 = no line), y and z narrowest and widest line (m), w depth push (m)

        float CodeFront(float3 positionWS)
        {
            float2 cell = floor(positionWS.xz * 24);
            return _CodeCutHeight + frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453) * 0.06;
        }
        ENDHLSL

        // Ink line: back faces pushed out along smoothed normals by a constant number of screen pixels.
        // FighterInk writes the smoothed normals into the tangents of a mesh copy and marks them with w = 0;
        // a mesh without them (not readable) draws no line.
        Pass
        {
            Name "Ink"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex InkVert
            #pragma fragment InkFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 tangentOS : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings InkVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 smooth = input.tangentOS.xyz;
                bool inked = _FighterInk.x > 0.01 && abs(input.tangentOS.w) < 0.5 && dot(smooth, smooth) > 0.25;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(smooth);
                // The hull sits a little behind the body, so only the silhouette and real depth steps get a
                // line: hair spikes and fingers do not ink the face and chest behind them.
                float3 pushedWS = positionWS + normalize(positionWS - _WorldSpaceCameraPos) * _FighterInk.w;
                float4 positionCS = TransformWorldToHClip(pushedWS);
                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS);
                float2 dir = normalCS.xy / max(length(normalCS.xy), 1e-5);
                // A constant pixel width, kept between two world widths: a far fighter is not swallowed by
                // its line and a close-up keeps one.
                float pixelWorld = max(positionCS.w, 1e-4) * 2.0 / (_ScreenParams.y * abs(UNITY_MATRIX_P[1][1]));
                float widthPx = clamp(_FighterInk.x, _FighterInk.y / pixelWorld, _FighterInk.z / pixelWorld);
                positionCS.xy += dir * (widthPx * 2.0 / _ScreenParams.xy) * positionCS.w;
                // No line: every vertex lands on one point, so the triangles cover no pixel.
                output.positionCS = inked ? positionCS : float4(0.0, 0.0, 0.0, 1.0);
                output.positionWS = positionWS;
                return output;
            }

            half4 InkFrag(Varyings input) : SV_Target
            {
                clip(input.positionWS.y - CodeFront(input.positionWS));
                return half4(_FighterInkColor.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_instancing

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

            // Broad, soft steps retain the graphic look without cutting across faces.
            half3 Band(half wrappedLight)
            {
                half band = 0.35h + 0.40h * smoothstep(0.30h, 0.58h, wrappedLight)
                    + 0.25h * smoothstep(0.65h, 0.88h, wrappedLight);
                return lerp(_ShadowColor.rgb, half3(1, 1, 1), band);
            }

            half3 Toon(Light light, half3 normalWS)
            {
                half wrappedLight = saturate(dot(normalWS, light.direction) * 0.5h + 0.5h);
                half shadowAttenuation = lerp(0.45h, 1.0h, light.shadowAttenuation);
                return Band(wrappedLight) * light.color * (light.distanceAttenuation * shadowAttenuation);
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float front = CodeFront(input.positionWS);
                clip(input.positionWS.y - front);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(facing, 1.0h, -1.0h);
                // With a position, so shadows fade at the quality level's shadow distance (see StageVertexLit).
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, half4(1, 1, 1, 1));
                half wrappedLight = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 direct = Toon(mainLight, normalWS);

                // Stages light their sets with point lights; fighters take them too or read darker than the floor.
                half3 lamps = half3(0, 0, 0);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                uint lightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex)
                    lamps += Toon(GetAdditionalLight(lightIndex, input.positionWS), normalWS);
                #endif
                // The stage's own pools, then the flash of a hit, banded like every other light.
                half3 flashes = half3(0, 0, 0);
                int lookCount = (int)_StageLookLightCount;
                for (int look = 0; look < STAGE_LOOK_LIGHTS; look++)
                {
                    if (look >= lookCount) break;
                    half3 toLight;
                    half3 arriving = StageLookLight(look, input.positionWS, toLight);
                    half wrapped = saturate(dot(normalWS, toLight) * 0.5h + 0.5h);
                    if (_StageLookLightColors[look].a < 0.5) lamps += Band(wrapped) * arriving;
                    else flashes += arriving * smoothstep(0.35h, 0.7h, wrapped);
                }
                // A strong lamp over the playfield must not bleach the fighter standing under it.
                half peak = max(lamps.r, max(lamps.g, lamps.b));
                half cap = _FighterLampCap > 0.0h ? _FighterLampCap : 1000.0h;
                direct += lamps * min(1.0h, cap / max(peak, 1e-3h));

                half3 ambient = max(SampleSH(normalWS), half3(0, 0, 0));
                // Night stages use a weak key light; a fill from the camera keeps fighters readable on any stage.
                direct += _FighterFill * saturate(dot(normalWS, viewDir) * 0.5h + 0.5h);
                half3 color = albedo.rgb * (ambient + direct) * (1.0h + _FighterLift);

                // A restrained satin highlight gives volume without a wet/plastic finish.
                half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                half highlight = pow(saturate(dot(normalWS, halfDir)), 32.0h)
                    * smoothstep(0.45h, 0.75h, wrappedLight);
                color += mainLight.color * highlight * 0.16h * mainLight.shadowAttenuation;

                // Keep player colour on the silhouette, not across the entire material.
                half facingAway = saturate(1.0h - dot(normalWS, viewDir));
                half rim = pow(facingAway, _RimPower + 1.5h) * _RimStrength * 0.4h;
                color += _RimColor.rgb * rim;

                // Stage kick lights: an edge of the stage's own colours on the sides that face them.
                half edge = pow(facingAway, max(_FighterKickPower, 0.5h));
                half kickA = smoothstep(-0.1h, 0.6h, dot(normalWS, _FighterKickDirA.xyz)) * _FighterKickColorA.a;
                half kickB = smoothstep(-0.1h, 0.6h, dot(normalWS, _FighterKickDirB.xyz)) * _FighterKickColorB.a;
                color += (_FighterKickColorA.rgb * kickA + _FighterKickColorB.rgb * kickB) * edge * (0.55h + 0.45h * albedo.rgb);

                color = StageLookFlash(color, albedo.rgb, flashes);
                color = lerp(color, _FlashColor.rgb, _FlashAmount);
                if (input.positionWS.y < front + 0.065)
                    color = frac(input.positionWS.y * 42) < 0.4 ? half3(1, 1, 1) : half3(0.569, 1, 0.412);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.world);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                clip(input.world.y - _CodeCutHeight);
                return 0;
            }
            ENDHLSL
        }

        // The camera's depth texture (far blur) when URP draws it in a pass of its own.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.world);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                clip(input.world.y - CodeFront(input.world));
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
