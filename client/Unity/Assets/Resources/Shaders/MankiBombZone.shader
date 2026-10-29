Shader "SLOPArena/Manki Bomb Zone"
{
    Properties
    {
        _Progress("Time to blast", Range(0, 1)) = 0
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
                float _Progress;
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
                float r = length(input.uv * 2 - 1);
                float aa = max(fwidth(r), .002);
                float disk = 1 - smoothstep(1 - aa, 1, r);
                // Fixed outer edge is the blast radius; the second ring closes toward impact.
                float boundary = smoothstep(.974 - aa, .974, r) * disk;
                float closing = 1 - smoothstep(.012, .012 + aa, abs(r - (1 - _Progress) * .96));
                float alpha = max(boundary * .6, closing * .5) * disk;
                alpha = max(alpha, disk * lerp(.035, .075, _Progress));
                return half4(1, .5, .03, alpha);
            }
            ENDHLSL
        }
    }
}
