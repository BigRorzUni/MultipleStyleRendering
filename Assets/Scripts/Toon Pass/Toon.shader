Shader "Custom/Toon"
{
    Properties
    {
        _StylisedMask ("Style Mask", float) = 0

        // take in unity's base properties
        _BaseTex ("Main Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }

        Pass
        {
            Name "Toon"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseTex);
            SAMPLER(sampler_BaseTex);

            CBUFFER_START(UnityPerMaterial)
                float  _StylisedMask;
                float4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                uint mask = (uint)round(_StylisedMask);
                const uint TOON_BIT = 1u << 1;

                if ((mask & TOON_BIT) == 0u)
                    discard;

                float4 tex = SAMPLE_TEXTURE2D(_BaseTex, sampler_BaseTex, i.uv);

                return tex * _BaseColor;
            }
            ENDHLSL
        }
    }
}