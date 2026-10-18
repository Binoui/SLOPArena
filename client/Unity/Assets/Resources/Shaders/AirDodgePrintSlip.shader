Shader "SLOPArena/Air Dodge Print Slip"
{
    // A flat-colour copy of a fighter mesh, shifted on screen and pushed back in depth so it
    // only shows where it peeks out from behind the fighter: the air dodge's misprint. The
    // shift follows _Slip (back along the dodge) as each camera sees it at _Anchor, with a
    // constant on-screen length, so a dodge toward the camera still slips sideways.
    Properties
    {
        _Color("Colour", Color) = (1, 0.8, 0.133, 1)
        _Anchor("Anchor (world)", Vector) = (0, 0, 0, 0)
        _Slip("Slip (world metres)", Vector) = (0, 0, 0, 0)
        _DepthPush("Depth push (m)", Float) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Anchor;
                float4 _Slip;
                float _DepthPush;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            float2 Ndc(float3 positionWS)
            {
                float4 clip = TransformWorldToHClip(positionWS);
                return clip.xy / clip.w;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                // Push the copy away along the view ray, which keeps its place on screen
                // (an orthographic view looks straight down -Z).
                if (unity_OrthoParams.w < 0.5)
                    positionVS *= 1.0 + _DepthPush / max(length(positionVS), 1e-3);
                else
                    positionVS.z -= _DepthPush;
                float4 positionCS = TransformWViewToHClip(positionVS);

                float slipLength = length(_Slip.xyz);
                float2 anchor = Ndc(_Anchor.xyz);
                float2 across = Ndc(_Anchor.xyz + UNITY_MATRIX_V[0].xyz * slipLength) - anchor;
                float2 shift = Ndc(_Anchor.xyz + _Slip.xyz) - anchor;
                float onScreen = length(across);
                // Keep the on-screen length; a dodge along the view axis falls back to sliding left.
                shift = length(shift) > onScreen * 0.3 ? normalize(shift) * onScreen : -across;
                positionCS.xy += shift * positionCS.w;
                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
