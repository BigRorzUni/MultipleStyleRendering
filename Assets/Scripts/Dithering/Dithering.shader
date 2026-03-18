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

            float4 _Rect;  // xy origin, zw width height
            float2 _ScreenTexelSize;


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
                return (uint)round(saturate(m) * 255.0); // unnormalise texture
            }

            static const uint Bayer8x8[8*8] =
            {
                0, 32, 8, 40, 2, 43, 10, 42,
                48, 16, 56, 24, 50, 18, 58, 26,
                12, 44, 4, 36, 14, 46, 6, 38, 
                60, 28, 52, 20, 62, 30, 54, 22,
                3, 35, 11, 43, 1, 33, 9, 41,
                51, 19, 59, 27, 49, 17, 57, 25,
                15, 47, 7, 39, 13, 45, 5, 37,
                63, 31, 55, 23, 61, 29, 53, 21 
            };

            float4 Frag (Varyings i) : SV_Target
            {
                // TODO: dither across all colour channels
                // get colour over bbox texture
                float4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, i.uv);
                uint mask = ReadMask8(i.uv);

                // if pixels aren't tagged for dithering then leave them unchanged
                const uint DITHERING_BIT = 1u << 1; // change this to a uniform
                if ((mask & DITHERING_BIT) == 0u)
                    return col;

                // TODO: move this to an object space shader
                // convert pixels to greyscale 
                // https://scikit-image.org/docs/stable/auto_examples/color_exposure/plot_rgb_to_gray.html
                // float greyscale = dot(col.rgb, float3(0.2125, 0.7154, 0.0721));

                uint2 pixelXY = (uint2)(i.uv * _SourceTex_TexelSize.zw);

                // flatten pixelXY
                pixelXY = pixelXY % 8;
                uint idx = pixelXY.y * 8 + pixelXY.x;

                // get 
                float threshold = (Bayer8x8[idx] + 0.5) / 64.0;

                float outR = step(threshold, col.r);
                float outG = step(threshold, col.g);
                float outB = step(threshold, col.b);

                return float4(outR, outG, outB, col.a);
            }
            ENDHLSL
        }
    }
}