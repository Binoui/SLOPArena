// Presentation-only night city surface: vertex-colour albedo (baked AO included) under a
// cheap ambient base, plus emission where vertex alpha x atlas alpha marks glowing parts.
// Per-vertex channels animate signs and traffic without scripts; gameplay never reads them.
//   TEXCOORD1 (rate, phase): 0 steady | (0,50) blink Hz | [50,100) pulse Hz-50 |
//             [100,150) chase Hz-100 (one third on) | [150,200) slow toggle Hz-150 | < 0 flicker
//   TEXCOORD2 (speed, along): m/s along the travel direction; rest position along it
//   TEXCOORD3 (dir * loop):   horizontal direction scaled by the wrap length in metres
//   TEXCOORD3 = 0 with TEXCOORD2 (height, Hz * 10 + phase): bob, a vertical hop (crowds)
Shader "SLOPArena/StageNeon"
{
    Properties
    {
        [MainTexture] _BaseMap("Atlas (A = glow mask)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Base("Unlit Base Level", Range(0, 2)) = 0.35
        _Ambient("Ambient (SH) Scale", Range(0, 2)) = 0.6
        _ShadeDirection("Shade Direction", Vector) = (0.35, 0.8, 0.45, 0)
        _Shade("Directional Shade", Range(0, 1)) = 0.35
        _Glow("Glow", Range(0, 16)) = 3
        _FogAmount("Fog Amount", Range(0, 1)) = 1
        _TimeScale("Animation Speed", Range(0, 4)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Base;
            half _Ambient;
            float4 _ShadeDirection;
            half _Shade;
            half _Glow;
            half _FogAmount;
            float _TimeScale;
            half _Cull;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        float StageTime() { return _Time.y * _TimeScale; }

        // Looping travel along a horizontal lane centred on the world origin, or, without a
        // direction, a bouncing hop of travel.x metres at floor(travel.y) / 10 Hz.
        float3 StageTravel(float3 positionWS, float2 travel, float2 dirLoop)
        {
            if (abs(travel.x) < 1e-4) return positionWS;
            float loop = length(dirLoop);
            if (loop < 1e-3)
            {
                float hz = floor(travel.y) * 0.1;
                positionWS.y += travel.x * abs(sin(3.14159265 * (StageTime() * hz + frac(travel.y))));
                return positionWS;
            }
            float2 dir = dirLoop / loop;
            float along = travel.y + travel.x * StageTime();
            float wrapped = along - loop * floor(along / loop + 0.5);
            positionWS.xz += dir * (wrapped - travel.y);
            return positionWS;
        }

        float Hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

        half StageAnimation(float2 channel)
        {
            float rate = channel.x;
            float phase = channel.y;
            float t = StageTime();
            if (rate == 0.0) return 1.0h;
            if (rate < 0.0)
            {
                // Faulty tube: long steady spells, then a burst of buzzing drop-outs.
                float burst = step(0.72, Hash11(floor(t * 0.35 + phase * 17.0)));
                float drop = step(Hash11(floor(t * -rate * 9.0 + phase * 61.0)), 0.45);
                return (half)lerp(1.0, 0.12, burst * drop);
            }
            if (rate < 50.0)
            {
                float x = frac(t * rate + phase);
                return (half)(smoothstep(0.0, 0.04, x) * (1.0 - smoothstep(0.5, 0.54, x)));
            }
            if (rate < 100.0)
                return (half)(0.55 + 0.45 * sin((t * (rate - 50.0) + phase) * 6.2831853));
            if (rate < 150.0)
                return (half)step(frac(t * (rate - 100.0) + phase), 0.3333);
            // Lit windows: a few switch over long periods.
            float slot = floor(t * (rate - 150.0) + phase * 7.0);
            return (half)lerp(0.08, 1.0, step(0.22, Hash11(slot + phase * 131.0)));
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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 anim : TEXCOORD1;
                float2 travel : TEXCOORD2;
                float2 dirLoop : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half4 color : COLOR;
                float2 uv : TEXCOORD1;
                half anim : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = StageTravel(positionWS, input.travel, input.dirLoop);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                half4 color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                color.rgb = SRGBToLinear(color.rgb);
                #endif
                output.color = color;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.anim = StageAnimation(input.anim);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = input.color.rgb * tex.rgb * _BaseColor.rgb;
                half facing = saturate(dot(normalWS, normalize(_ShadeDirection.xyz)) * 0.5h + 0.5h);
                half3 base = _Base * lerp(1.0h, facing, _Shade) + max(SampleSH(normalWS), half3(0, 0, 0)) * _Ambient;
                half glow = input.color.a * tex.a;
                half3 color = albedo * base + albedo * (glow * _Glow * input.anim);
                color = lerp(color, MixFog(color, input.fogFactor), _FogAmount);
                return half4(color, 1.0h);
            }
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
                float2 travel : TEXCOORD2;
                float2 dirLoop : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = StageTravel(TransformObjectToWorld(input.positionOS.xyz), input.travel, input.dirLoop);
                return TransformWorldToHClip(positionWS);
            }

            half DepthFrag() : SV_Target { return 0; }
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
                float2 travel : TEXCOORD2;
                float2 dirLoop : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            NormalsVaryings NormalsVert(NormalsAttributes input)
            {
                NormalsVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = StageTravel(TransformObjectToWorld(input.positionOS.xyz), input.travel, input.dirLoop);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(NormalsVaryings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
