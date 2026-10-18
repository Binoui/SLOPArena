Shader "SLOPArena/Air Dodge Print"
{
    // Procedural print shapes for the air dodge (AirDodgePresentation builds the quads):
    // a flat motion bar or a cut-paper crescent, each with an ink rim. Each quad lies along
    // the dodge and turns about it to face whichever camera draws it. A shadow quad is the
    // same shape in ink, nudged toward the bottom right of the screen.
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+12" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            static const float ShadowShift = 0.035;
            static const half3 Ink = half3(0.106, 0.102, 0.094);

            struct Attributes
            {
                float4 positionOS : POSITION;   // the quad's centre
                half4 color : COLOR;
                float2 uv : TEXCOORD0;          // the corner: x along the dodge, y across
                float4 shape : TEXCOORD1;       // length (m), thickness (m), shape (0 bar, 1 crescent), shadow flag
                float3 axis : TEXCOORD2;        // the dodge direction in world space
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 shape : TEXCOORD1;       // aspect, shape, rim width
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 center = TransformObjectToWorld(input.positionOS.xyz);
                float3 axis = normalize(input.axis);
                float3 side = cross(axis, _WorldSpaceCameraPos.xyz - center);
                float sideLength = length(side);
                side = sideLength > 1e-5 ? side / sideLength : float3(0.0, 1.0, 0.0);
                float3 world = center
                    + axis * ((input.uv.x - 0.5) * input.shape.x)
                    + side * ((input.uv.y - 0.5) * input.shape.y);
                if (input.shape.w > 0.5)
                    world += (UNITY_MATRIX_V[0].xyz - UNITY_MATRIX_V[1].xyz) * ShadowShift;
                output.positionCS = TransformWorldToHClip(world);
                output.color = input.shape.w > 0.5 ? half4(Ink, input.color.a * 0.6) : input.color;
                output.uv = input.uv;
                float rim = input.shape.z < 0.5 ? 0.17 : 0.028;
                output.shape = float3(input.shape.x / max(input.shape.y, 1e-4), input.shape.z, rim);
                return output;
            }

            // Signed distance in quad heights; x runs along the dodge, y across.
            float ShapeDistance(float2 p, float aspect, float shape)
            {
                if (shape < 0.5)
                {
                    float radius = 0.38;
                    float halfLength = max(aspect * 0.5 - radius - 0.04, 0.0);
                    float2 q = float2(max(abs(p.x) - halfLength, 0.0), p.y);
                    return length(q) - radius;
                }
                // Crescent bulging back, its horns pointing along the dodge.
                float outer = length(p) - 0.46;
                float inner = length(p - float2(0.18, 0.0)) - 0.41;
                return max(outer, -inner);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float aspect = input.shape.x;
                float2 p = float2((input.uv.x - 0.5) * aspect, input.uv.y - 0.5);
                float d = ShapeDistance(p, aspect, input.shape.y);
                float aa = max(fwidth(d), 1e-4);
                float inside = 1.0 - smoothstep(-aa, aa, d);
                float fill = 1.0 - smoothstep(-aa, aa, d + input.shape.z);
                half3 color = lerp(Ink, input.color.rgb, fill);
                return half4(color, inside * input.color.a);
            }
            ENDHLSL
        }
    }
}
