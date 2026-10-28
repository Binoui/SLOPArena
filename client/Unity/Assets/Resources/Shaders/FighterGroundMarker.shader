Shader "SLOPArena/Fighter Ground Marker"
{
    Properties
    {
        _Airborne("Airborne visibility", Range(0, 1)) = 0
        _Accent("Fighter accent", Color) = (0.941, 0.933, 0.910, 1)
        _CampColor("Camp colour and visibility", Color) = (0, 0, 0, 0)
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
                half4 _CampColor;
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

                // Every fighter wears the same facing crescent and arrow in their seat accent;
                // a camp or course colour adds the inner ring.
                float arrowDistance = max(abs(floorUv.x) - (0.96 - floorUv.y) * 0.60,
                    max(0.59 - floorUv.y, floorUv.y - 0.96));
                float front = saturate(0.5 + 0.5 * floorUv.y / max(radius, 0.001));
                float taper = pow(front, 1.3);
                float fade = smoothstep(0.06, 0.52, front);
                float crescentDistance = abs(radius - 0.66) - lerp(0.002, 0.10, taper);
                float facingBorder = max((1.0 - smoothstep(-edge, edge, crescentDistance - 0.018)) * fade,
                    1.0 - smoothstep(-edge, edge, arrowDistance - 0.018));
                float facingCore = max((1.0 - smoothstep(-edge, edge, crescentDistance)) * fade,
                    1.0 - smoothstep(-edge, edge, arrowDistance));
                float campRim = (1.0 - smoothstep(0.018 - edge, 0.018 + edge, abs(radius - 0.50))) * _CampColor.a * 0.65;
                float rimAlpha = max(facingBorder * 0.96, campRim);
                half3 ringColor = lerp(_CampColor.rgb, lerp(half3(0.10, 0.075, 0.025), _Accent.rgb,
                    facingCore / max(facingBorder, 0.0001)), saturate(facingBorder));

                float alpha = rimAlpha + shadow * (1.0 - rimAlpha);
                half3 color = (ringColor * rimAlpha + half3(0.08, 0.075, 0.065)
                    * shadow * (1.0 - rimAlpha)) / max(alpha, 0.0001);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
