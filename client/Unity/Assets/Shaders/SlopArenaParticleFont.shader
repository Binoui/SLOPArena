Shader "SLOPArena/Particles/Font"
{
    Properties
    {
        [MainTexture] _BaseMap("Font Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
                float4 custom2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half4 custom1 : TEXCOORD1;
                half4 custom2 : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformWorldToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.texcoord.xy, _BaseMap);
                output.color = input.color * _BaseColor;
                output.custom1 = input.custom1;
                output.custom2 = input.custom2;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 fontTexture = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // CFXR_ParticleText encodes the three font colors in:
                // texture blue = background/particle color,
                // texture green = Custom1/color1,
                // texture red = Custom2/color2.
                half3 color = fontTexture.b * input.color.rgb
                            + fontTexture.g * input.custom1.rgb
                            + fontTexture.r * input.custom2.rgb;
                half alpha = fontTexture.a * input.color.a;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
