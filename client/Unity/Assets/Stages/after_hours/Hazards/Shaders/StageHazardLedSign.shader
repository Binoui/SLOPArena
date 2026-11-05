// Presentation only: the DJ's countdown as a floating seven-segment LED sign that always faces the
// camera. The mesh is flat in the object's XY plane in metres around the object's origin; the vertex
// stage re-plants it in view space at that origin, so it never reads as a floor or a ledge.
// UV0 is glyph space (x 0..0.62, y 0..1). UV1.x is the lit segment mask (a=1 b=2 c=4 d=8 e=16 f=32
// g=64), UV1.y the kind (0 glyph, 1 backing panel). The vertex colour is the tint (linear) and its
// alpha the brightness.
Shader "SlopArena/StageHazardLedSign"
{
    Properties
    {
        _PanelColor("Panel", Color) = (0.03, 0.01, 0.06, 0.62)
        _FrameColor("Panel frame", Color) = (1, 0.3, 0.75, 1)
        _Ghost("Unlit segment ghost", Range(0, 0.4)) = 0.1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Sign"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _PanelColor, _FrameColor;
                half _Ghost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 data : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4x4 m = GetObjectToWorldMatrix();
                float scale = length(float3(m._m00, m._m10, m._m20));
                float3 view = TransformWorldToView(float3(m._m03, m._m13, m._m23));
                view.xy += input.positionOS.xy * scale;
                output.positionCS = TransformWViewToHClip(view);
                output.color = input.color;
                output.uv = input.uv;
                output.data = input.data;
                return output;
            }

            float Segment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv;
                if (input.data.y > 0.5)
                {
                    // Backing panel: p runs 0..1 across it; a rounded dark plate with a thin neon frame.
                    float2 q = abs(p - 0.5) * 2.0;
                    float edge = max(q.x, q.y);
                    half frame = smoothstep(0.93, 0.955, edge) * (1.0h - smoothstep(0.985, 1.0, edge));
                    half inside = 1.0h - smoothstep(0.985, 1.0, edge);
                    half3 color = lerp(_PanelColor.rgb, _FrameColor.rgb * input.color.a, frame);
                    return half4(color, max(_PanelColor.a * inside, frame));
                }
                const float w = 0.62, m = 0.1, t = 0.03, r = 0.075;
                float2 s[14] = {
                    float2(m + t, 1 - m), float2(w - m - t, 1 - m),      // a
                    float2(w - m, 1 - m - t), float2(w - m, 0.5 + t),    // b
                    float2(w - m, 0.5 - t), float2(w - m, m + t),        // c
                    float2(m + t, m), float2(w - m - t, m),              // d
                    float2(m, 0.5 - t), float2(m, m + t),                // e
                    float2(m, 1 - m - t), float2(m, 0.5 + t),            // f
                    float2(m + t, 0.5), float2(w - m - t, 0.5) };        // g
                uint mask = (uint)(input.data.x + 0.5);
                half on = 0, ghost = 0, glow = 0;
                [unroll] for (int i = 0; i < 7; i++)
                {
                    float d = Segment(p, s[2 * i], s[2 * i + 1]);
                    half body = 1.0h - smoothstep(r * 0.75, r, d);
                    if ((mask >> i) & 1u) { on = max(on, body); glow = max(glow, exp(-d * 22.0)); }
                    else ghost = max(ghost, body);
                }
                half bright = input.color.a;
                half3 core = lerp(input.color.rgb, half3(1, 0.92, 0.8), 0.6h * smoothstep(0.5h, 1.0h, on));
                half alpha = saturate(max(max(on, glow * 0.75h) * bright, ghost * _Ghost));
                half3 color = on > 0.001h || glow * bright > ghost * _Ghost ? core : input.color.rgb * 0.5h;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
