Shader "SlopArena/Mayhem Beam"
{
    Properties { _BaseColor ("Beam colour", Color) = (1, .7, .2, .035) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float fade : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.fade = 1 - input.positionOS.z * .85;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return half4(_BaseColor.rgb, _BaseColor.a * input.fade); }
            ENDHLSL
        }
    }
}
