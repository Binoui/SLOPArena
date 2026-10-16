Shader "SLOPArena/Fighter Ground Marker"
{
    Properties
    {
        _Airborne("Airborne visibility", Range(0, 1)) = 0
        _Accent("Local fighter notch", Color) = (0.941, 0.933, 0.910, 1)
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
                half4 _Accent;
                half _Airborne;
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
                float2 floorUv = input.uv * 2.0 - 1.0;
                float radius = length(floorUv);
                float edge = max(fwidth(radius), 0.002);
                // The floor anchor never expands with jump height. Only the shadow softens.
                float shadowRadius = lerp(0.75, 0.92, _Airborne);
                float shadow = pow(saturate(1.0 - radius / shadowRadius), 2.0)
                    * lerp(0.28, 0.16, _Airborne);

                float angle = atan2(floorUv.y, floorUv.x);
                float arc = cos(angle * 4.0);
                float arcEdge = max(fwidth(arc), 0.01);
                float segments = smoothstep(-0.24 - arcEdge, -0.24 + arcEdge, arc);
                float distanceToRing = abs(radius - 0.68);
                float border = (1.0 - smoothstep(0.062 - edge, 0.062 + edge, distanceToRing)) * segments;
                float rim = (1.0 - smoothstep(0.026 - edge, 0.026 + edge, distanceToRing)) * segments;
                float notch = smoothstep(0.975, 0.990, sin(angle));
                float visibility = lerp(0.12, 0.95, _Airborne);
                float rimAlpha = border * visibility;
                half3 ringColor = lerp(half3(0.106, 0.102, 0.094),
                    lerp(half3(0.941, 0.933, 0.910), _Accent.rgb, notch), rim);

                // Combine the shadow and outline in one transparent pass, with an open centre.
                float alpha = rimAlpha + shadow * (1.0 - rimAlpha);
                half3 color = (ringColor * rimAlpha + half3(0.08, 0.075, 0.065)
                    * shadow * (1.0 - rimAlpha)) / max(alpha, 0.0001);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
