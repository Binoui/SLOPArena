Shader "SLOPArena/Manki Toon Volume"
{
    Properties
    {
        _BaseMap("Palette", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _VertexPalette("Vertex palette", Float) = 0
        _SootBands("Soot bands", Float) = 0
        _VolumeDepthWrite("Depth write", Float) = 1
        _VolumeCull("Cull", Float) = 0
        _ToonBands("Colour bands", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_VolumeDepthWrite]
            Cull [_VolumeCull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _VertexPalette, _SootBands, _ToonBands;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 positionWS:TEXCOORD1; float3 positionOS:TEXCOORD3; float2 uv:TEXCOORD2; half4 color:COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                color *= lerp(half4(1,1,1,1), input.color, _VertexPalette);
                if (_SootBands > .5)
                {
                    float3 normal = normalize(input.normalWS);
                    float facing = saturate(dot(normal, normalize(_WorldSpaceCameraPos - input.positionWS)));
                    float light = dot(normal, normalize(float3(-.4,.8,-.3)));
                    float seam = input.positionOS.y - .22 * facing + .055 * sin(input.positionOS.x * 17 + input.positionOS.z * 13);
                    half3 soot = light < .15 ? half3(.12,.10,.11) : light < .65 ? half3(.23,.19,.19) : half3(.33,.28,.26);
                    half3 fire = seam < -.19 ? half3(1,.82,.22) : seam < -.035 ? half3(1,.46,.055) : half3(.98,.25,.025);
                    color.rgb = facing < .18 ? half3(.075,.045,.04) : seam < .12 ? fire : soot;
                    color.rgb *= _BaseColor.rgb;
                    color.a *= .88;
                }
                else if (_ToonBands > .5)
                {
                    float facing = abs(dot(normalize(input.normalWS), normalize(_WorldSpaceCameraPos - input.positionWS)));
                    color.rgb = facing < .25 ? half3(.075,.045,.03) : facing > .92 ? lerp(color.rgb, half3(1,.965,.84), .75) : color.rgb;
                }
                return color;
            }
            ENDHLSL
        }
    }
}
