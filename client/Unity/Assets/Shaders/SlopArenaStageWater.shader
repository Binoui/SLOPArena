// Presentation-only sea: vertex-colour depth tint, a gentle analytic swell, two scrolling
// ripple normal maps, sky reflection by fresnel, sun glitter and lapping foam where vertex
// alpha marks surf (hull and shores). Gameplay never reads this surface; the swell is
// limited to _SwellHeight and fades out with distance from the stage.
Shader "SLOPArena/StageWater"
{
    Properties
    {
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _RippleMap("Ripple Normals (R = x, G = z, B = up)", 2D) = "gray" {}
        _RippleScale("Ripple Scale (1/m)", Float) = 0.11
        _RippleStrength("Ripple Strength", Range(0, 2)) = 0.7
        _RippleSpeed("Ripple Speed (m/s)", Float) = 0.35
        _SwellHeight("Swell Height (m)", Range(0, 0.5)) = 0.12
        _SwellLength("Swell Length (m)", Float) = 11
        _SwellSpeed("Swell Speed", Float) = 0.9
        _SwellFade("Swell Fade Distance (m)", Float) = 140
        _SkyColor("Sky Reflection", Color) = (0.42, 0.62, 0.88, 1)
        _HorizonColor("Horizon Reflection", Color) = (0.80, 0.88, 0.94, 1)
        _Fresnel("Reflection", Range(0, 1)) = 0.75
        _Diffuse("Body Lighting", Range(0, 2)) = 0.85
        _Specular("Sun Glitter", Range(0, 8)) = 2.4
        _Gloss("Glitter Tightness", Range(16, 4096)) = 900
        _FoamColor("Foam", Color) = (0.95, 0.97, 0.98, 1)
        _FoamStrength("Foam Strength", Range(0, 1)) = 0.85
        _FoamSpeed("Foam Lapping Speed", Float) = 1.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _RippleMap_ST;
            float _RippleScale;
            half _RippleStrength;
            float _RippleSpeed;
            float _SwellHeight;
            float _SwellLength;
            float _SwellSpeed;
            float _SwellFade;
            half4 _SkyColor;
            half4 _HorizonColor;
            half _Fresnel;
            half _Diffuse;
            half _Specular;
            half _Gloss;
            half4 _FoamColor;
            half _FoamStrength;
            float _FoamSpeed;
        CBUFFER_END

        TEXTURE2D(_RippleMap); SAMPLER(sampler_RippleMap);

        // Two crossing swells; returns height and its slope (d/dx, d/dz) in world space.
        float3 Swell(float2 xz)
        {
            float t = _Time.y * _SwellSpeed;
            float k1 = 6.2831853 / max(_SwellLength, 0.5);
            float k2 = k1 * 1.63;
            float2 d1 = normalize(float2(-0.94, 0.34));
            float2 d2 = normalize(float2(-0.42, -0.91));
            float p1 = dot(d1, xz) * k1 + t;
            float p2 = dot(d2, xz) * k2 + t * 1.31;
            float fade = saturate(1.0 - length(xz - float2(2.5, 0.0)) / max(_SwellFade, 1.0));
            float a1 = _SwellHeight * 0.65 * fade;
            float a2 = _SwellHeight * 0.35 * fade;
            float h = a1 * sin(p1) + a2 * sin(p2);
            float2 slope = a1 * k1 * cos(p1) * d1 + a2 * k2 * cos(p2) * d2;
            return float3(h, slope);
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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : COLOR;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += Swell(positionWS.xz).x;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                half4 color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                color.rgb = SRGBToLinear(color.rgb);
                #endif
                output.color = color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half3 Ripple(float2 uv)
            {
                half3 t = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uv).rgb * 2.0h - 1.0h;
                return half3(t.r, t.b, t.g); // stored as x, z, up
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                float3 swell = Swell(p.xz);
                float dist = length(p.xz - float2(2.5, 0.0));
                float t = _Time.y * _RippleSpeed;
                float2 uv = p.xz * _RippleScale;
                half3 r1 = Ripple(uv + float2(0.71, 0.23) * t);
                half3 r2 = Ripple(uv * 1.73 + float2(-0.31, 0.77) * t * 1.21);
                half k = _RippleStrength * lerp(1.0h, 0.3h, (half)saturate(dist / 700.0));
                half3 normalWS = normalize(half3(-swell.y + (r1.x + r2.x) * k, 1.0h, -swell.z + (r1.z + r2.z) * k));

                half3 albedo = input.color.rgb * _BaseColor.rgb;
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(p));
                half shadowed = mainLight.shadowAttenuation;
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(p));
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = max(SampleSH(half3(0, 1, 0)), half3(0, 0, 0));
                half3 body = albedo * (ambient + mainLight.color * (0.35h + 0.65h * ndl) * shadowed) * _Diffuse;

                half3 refl = reflect(-viewDir, normalWS);
                half3 sky = lerp(_HorizonColor.rgb, _SkyColor.rgb, saturate(refl.y * 2.2h));
                half fresnel = 0.02h + 0.98h * pow(1.0h - saturate(dot(normalWS, viewDir)), 5.0h);
                half3 color = lerp(body, sky * lerp(0.75h, 1.0h, shadowed), saturate(fresnel * _Fresnel + 0.12h * _Fresnel));

                half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                half glint = pow(saturate(dot(normalWS, halfDir)), _Gloss) * _Specular * shadowed;
                color += mainLight.color * glint;

                // Lapping foam: the marked band breathes in and out and breaks up with the ripples.
                half lap = 0.72h + 0.28h * sin(_Time.y * _FoamSpeed + dot(p.xz, float2(0.37, 0.21)));
                half breakup = saturate(0.5h + 0.5h * (r1.x * 1.7h - r2.z * 1.3h));
                half foam = saturate((input.color.a * lap * 1.25h - breakup * 0.55h) * 2.6h) * _FoamStrength;
                half3 foamLit = _FoamColor.rgb * (ambient + mainLight.color * (0.4h + 0.6h * ndl) * shadowed);
                color = lerp(color, foamLit, foam);

                color = MixFog(color, input.fogFactor);
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += Swell(positionWS.xz).x;
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing

            struct NormalsAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            NormalsVaryings NormalsVert(NormalsAttributes input)
            {
                NormalsVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += Swell(positionWS.xz).x;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 NormalsFrag(NormalsVaryings input) : SV_Target
            {
                return half4(0.0h, 1.0h, 0.0h, 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
