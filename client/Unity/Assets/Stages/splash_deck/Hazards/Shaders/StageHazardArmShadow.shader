// Presentation only: the kraken arm's shadow sweeping across a slam lane before it lands. Drawn
// as the back faces of a box around the lane; each pixel rebuilds the visible surface from the
// camera depth texture, so the shadow drapes over the deck, the wheelhouse or hatch roof and the
// fighters, and never floats. Its outline is the lying arm's plan shape (full lane width, tapering
// to the tip past the far rail, a gentle wobble, soft edges), not the hit rectangle. _Reach grows
// it from the near rail; _Strength darkens it. Both come from the simulation tick.
Shader "SlopArena/StageHazardArmShadow"
{
    Properties
    {
        _ShadowColor("Shadow colour", Color) = (0.3, 0.27, 0.45, 1)
        _Strength("Strength", Range(0, 1)) = 0
        _Reach("Reach (0 near rail, 1 far rail)", Range(0, 1.2)) = 0
        _LaneX("Lane centre x", Float) = 0
        _Side("Near side (+1 port, -1 starboard)", Float) = 1
        _HalfBeam("Deck half beam at the lane", Float) = 11
        _HalfWidth("Arm half width", Float) = 4.25
        _DeckY("Deck top", Float) = 0
        _MaxHeight("Highest surface it falls on, above the deck", Float) = 1.75
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-30" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ArmShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor Zero
            ZWrite Off
            ZTest GEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _Strength;
                float _Reach;
                float _LaneX;
                float _Side;
                float _HalfBeam;
                float _HalfWidth;
                float _DeckY;
                float _MaxHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
                #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif
                float3 p = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float height = p.y - _DeckY;
                // u runs from the near rail (0) across the deck to the far rail (1).
                float u = (_HalfBeam - _Side * p.z) / (2.0 * _HalfBeam);
                float taper = pow(saturate(1.0 - smoothstep(0.5, 1.06, u)), 1.25);
                float halfWidth = _HalfWidth * (0.12 + 0.88 * taper) * (1.0 + 0.06 * sin(u * 9.0 + 0.7));
                float centre = _LaneX + 0.35 * sin(u * 6.3 + 1.1);
                float across = abs(p.x - centre);
                half body = 1.0 - smoothstep(halfWidth - 1.1, halfWidth + 0.15, across);
                half reach = 1.0 - smoothstep(_Reach - 0.1, _Reach + 0.015, u);
                half ends = smoothstep(-0.06, 0.02, u) * (1.0 - smoothstep(1.04, 1.1, u));
                half low = smoothstep(-0.6, -0.2, height) * (1.0 - smoothstep(_MaxHeight - 0.3, _MaxHeight, height));
                half shade = _Strength * body * reach * ends * low;
                return half4(lerp(half3(1, 1, 1), _ShadowColor.rgb, shade), 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
