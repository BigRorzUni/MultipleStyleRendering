Shader "Custom/Dithering"
{
    Properties
    {
        _SourceTex("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Dithering"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            TEXTURE2D(_NprIdTexture);
            SAMPLER(sampler_NprIdTexture);

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);
            float4 _SourceTex_TexelSize;


            CBUFFER_START(UnityPerMaterial)

            CBUFFER_END

            struct Attributes 
            { 
                uint vertexID : SV_VertexID; 
            };

            struct Varyings  
            { 
                float4 posCS : SV_POSITION; 
                float2 uv : TEXCOORD0; 
            };

            Varyings Vert (Attributes v)
            {
                Varyings o;
                o.posCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                return o;
            }

            uint ReadMask8(float2 uv)
            {
                float m = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_NprIdTexture, uv).r;
                return (uint)round(saturate(m) * 255.0);
            }

            static const uint Bayer4x4[16] =
            {
                0, 8, 2, 10,
                12, 4, 14, 6,
                3, 11, 1, 9,
                15, 7, 13, 5
            };

            float4 Frag (Varyings i) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, i.uv);

                // if pixels aren't tagged for dithering then leave them unchanged
                const uint DITHERING_BIT = 1u << 3;
                uint mask = ReadMask8(i.uv);
                if ((mask & DITHERING_BIT) == 0u)
                    return col;

                // convert pixels to greyscale 
                // https://scikit-image.org/docs/stable/auto_examples/color_exposure/plot_rgb_to_gray.html
                float greyscale = dot(col.rgb, float3(0.2125, 0.7154, 0.0721));

                uint2 pixelXY = (uint2)(i.uv * _SourceTex_TexelSize.zw);

                // flatten pixelXY
                pixelXY = pixelXY % 4;
                uint idx = pixelXY.y * 4 + pixelXY.x;

                // get 
                float threshold = (Bayer4x4[idx] + 0.5) / 16.0;

                float outV = step(threshold, greyscale);

                return float4(outV, outV, outV, col.a);

                //return float4(greyscale, greyscale, greyscale, col.a);
            }
            ENDHLSL
        }
    }
}