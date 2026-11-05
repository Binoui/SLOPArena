// Presentation only: round water droplets for the kraken's spray and drips. The mesh holds one
// quad per droplet: every corner sits at the droplet centre (world space) and UV0 names the corner,
// so the vertex stage turns it to face the camera. UV1.x is the droplet radius in metres; the
// vertex colour carries tint and fade. No textures, no lighting: a flat blob with a bright core.
Shader "SlopArena/StageHazardSpray"
{
    Properties
    {
        _Highlight("Core highlight", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Spray"
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
                half _Highlight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 corner : TEXCOORD0;
                float2 size : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 corner : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 view = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                view.xy += input.corner * input.size.x;
                output.positionCS = TransformWViewToHClip(view);
                half4 color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                color.rgb = SRGBToLinear(color.rgb);
                #endif
                output.color = color;
                output.corner = input.corner;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.corner);
                half alpha = (1.0h - smoothstep(0.62h, 1.0h, r)) * input.color.a;
                clip(alpha - 0.004h);
                half core = 1.0h - smoothstep(0.0h, 0.55h, length(input.corner - float2(-0.28, 0.3)));
                half3 color = lerp(input.color.rgb, half3(1, 1, 1), core * _Highlight);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
