// Presentation only: the Bass Drop pad overlay on the After Hours LED dance floor. One flat quad per
// 2 x 2 m pad, a hair above the baked floor tiles. UV0 runs -1..1 across the pad, so the drawing stops
// exactly at the pad's edge. The vertex colour is data, not colour: R = lit (danger) strength,
// G = dark (safe) strength, B = flash (0..0.5 toward the hot core on beats, 0.5..1 on to white at
// the drop), A = the build toward the drop.
// Lit pads glow hot with their four 1 m LED tiles, a bright frame and chevrons; dark pads go black.
Shader "SlopArena/StageHazardBassPads"
{
    Properties
    {
        _HotColor("Lit pad", Color) = (1, 0.07, 0.04, 1)
        _CoreColor("Lit pad core", Color) = (1, 0.55, 0.12, 1)
        _EdgeColor("Lit pad frame", Color) = (1, 0.88, 0.55, 1)
        _DarkColor("Dark pad", Color) = (0.015, 0.01, 0.03, 1)
        _Opacity("Lit opacity", Range(0, 1)) = 0.94
        _DarkOpacity("Dark opacity", Range(0, 1)) = 0.86
        _Phase("Chevron phase (set from the tick)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Pads"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HotColor, _CoreColor, _EdgeColor, _DarkColor;
                half _Opacity, _DarkOpacity;
                float _Phase;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 state : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.state = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half lit = input.state.r, dark = input.state.g, flash = input.state.b, build = input.state.a;
                float2 uv = input.uv;
                float2 a = abs(uv);
                float edge = max(a.x, a.y);
                // The pad's four 1 m LED tiles: dark seams through the middle and each tile's own soft centre.
                float2 tile = abs(frac(uv * 0.5 + 0.5) * 2.0 - 1.0);            // 0 at tile centre, 1 at its rim
                half seam = 1.0h - smoothstep(0.035, 0.07, min(a.x, a.y));
                half tileGlow = 1.0h - smoothstep(0.1, 1.0, max(tile.x, tile.y));
                // Chevrons pointing inward, drifting with the tick so the pad hums.
                half chevron = step(0.5, frac((a.x + a.y) * 1.25 - _Phase));
                half frame = smoothstep(0.84, 0.9, edge);
                half3 fill = lerp(_HotColor.rgb, _CoreColor.rgb, 0.45h * tileGlow * (0.3h + 0.7h * build));
                fill *= lerp(0.55h, 1.0h, chevron);
                fill = lerp(fill, _HotColor.rgb * 0.25h, seam * (1.0h - frame));
                fill = lerp(fill, _EdgeColor.rgb, frame);
                fill = lerp(fill, _CoreColor.rgb, saturate(flash * 2.0h) * (1.0h - frame * 0.5h));
                fill = lerp(fill, half3(1, 1, 1), saturate(flash * 2.0h - 1.0h));
                half litAlpha = saturate(lit) * _Opacity;
                half darkAlpha = saturate(dark) * _DarkOpacity;
                half alpha = litAlpha + darkAlpha * (1.0h - litAlpha);
                clip(alpha - 0.003h);
                half3 color = (fill * litAlpha + _DarkColor.rgb * darkAlpha * (1.0h - litAlpha)) / max(alpha, 1e-3h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
