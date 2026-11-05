// Presentation only: the Bass Drop blast. Additive quads standing on the lit pads' outline (walls)
// and square rings the size of one pad rising off it. UV0.y runs 0 at the floor to 1 at a wall's top;
// for a ring UV0 runs -1..1 across the pad and only its rim draws. UV1.x is the kind (0 wall, 1 ring).
// The vertex colour is the final linear tint times strength; nothing reaches past a lit pad.
Shader "SlopArena/StageHazardBassBlast"
{
    Properties
    {
        _Streaks("Streak count per metre", Float) = 7
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Blast"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Streaks;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 kind : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float3 kind : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.color = input.color;
                output.uv = input.uv;
                output.kind = float3(input.kind.x, (world.x + world.z) * _Streaks, 0);
                return output;
            }

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            half4 Frag(Varyings input) : SV_Target
            {
                half strength;
                if (input.kind.x < 0.5)
                {
                    float v = input.uv.y;
                    float cell = floor(input.kind.y);
                    half streak = 0.55h + 0.45h * Hash(cell);
                    half height = saturate(1.0 - v / lerp(0.55, 1.0, Hash(cell + 7.0)));
                    strength = pow(height, 1.4h) * streak + (1.0h - smoothstep(0.0, 0.08, v)) * 0.8h;
                }
                else
                {
                    float2 a = abs(input.uv);
                    float edge = max(a.x, a.y);
                    strength = smoothstep(0.78, 0.9, edge) * (1.0h - smoothstep(0.96, 1.0, edge));
                }
                return half4(input.color.rgb * strength * input.color.a, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
