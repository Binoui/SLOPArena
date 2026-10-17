Shader "SLOPArena/Fighter Drop Line"
{
    Properties
    {
        _Airborne("Airborne visibility", Range(0, 1)) = 0
        _LengthInWidths("Line length in line widths", Float) = 1
        _Spacing("Dot spacing in line widths", Float) = 2
        _Core("Light core radius in line widths", Range(0, 0.5)) = 0.3
        _Color("Dot colour", Color) = (1, 0.690, 0.231, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Airborne;
                float _LengthInWidths;
                float _Spacing;
                half _Core;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The line starts at the floor, so dots stay put while the fighter rises.
                // Spacing above one width keeps cell seams outside every dot.
                float spacing = max(_Spacing, 1.05);
                float along = input.uv.x * _LengthInWidths;
                float2 cell = float2((frac(along / spacing) - 0.5) * spacing, input.uv.y - 0.5);
                float radius = length(cell);
                float edge = max(fwidth(radius), 0.02);
                float disc = 1.0 - smoothstep(0.5 - edge, 0.5, radius);
                float core = 1.0 - smoothstep(_Core - edge, _Core + edge, radius);
                // Light dots with the floor ring's dark rim.
                half3 color = lerp(half3(0.106, 0.102, 0.094), _Color.rgb, core);
                return half4(color, disc * 0.95 * _Airborne);
            }
            ENDHLSL
        }
    }
}
