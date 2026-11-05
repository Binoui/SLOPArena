// Presentation only: the part of the kraken below the sea, seen through it. The accepted sea is
// opaque, so the kraken's own opaque pass stops at the waterline. This second material on the same
// skinned mesh draws only fragments below _WaterY, and writes the depth of the water surface on the
// pixel's view ray instead of its own. Ordinary depth testing then keeps the silhouette behind the
// deck, hull and fighters (they are nearer than the water there) and lets it through where the sea
// itself is the visible surface. It multiplies the sea by a purple tint that fades with depth.
Shader "SlopArena/StageHazardSubmerged"
{
    Properties
    {
        _Tint("Silhouette tint (multiplies the sea)", Color) = (0.42, 0.3, 0.55, 1)
        _WaterY("Water height (world)", Float) = -11.4
        _Clarity("Fade depth (m)", Float) = 7
        _Strength("Strength", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Submerged"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor Zero
            // The first silhouette fragment on a pixel writes the water depth; later ones on the same
            // ray land on (nearly) the same depth and fail, so overlapping arms do not darken twice.
            ZWrite On
            ZTest Less
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _WaterY;
                float _Clarity;
                half _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input, out float depth : SV_Depth) : SV_Target
            {
                float below = _WaterY - input.positionWS.y;
                clip(below - 0.02);
                float3 camera = _WorldSpaceCameraPos;
                float3 ray = normalize(input.positionWS - camera);
                clip(-ray.y - 1e-4);
                float t = (_WaterY - camera.y) / ray.y;
                clip(t);
                // A hair in front of the sea, so the sea's own depth never wins the test.
                float4 surface = TransformWorldToHClip(camera + ray * (t * 0.9995));
                #if UNITY_REVERSED_Z
                depth = surface.z / surface.w;
                #else
                depth = surface.z / surface.w * 0.5 + 0.5;
                #endif
                half visible = _Strength * exp(-below / max(_Clarity, 0.1));
                return half4(lerp(half3(1, 1, 1), _Tint.rgb, visible), 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
