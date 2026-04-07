Shader "Custom/DitheringBatched"
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
            Name "DitheringBatched"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct InstanceData
            {
                float4 rect; 
                int index;
            };

            StructuredBuffer<InstanceData> _InstanceData;
            float4 _NprScreenSize; 

            TEXTURE2D(_NprIdTexture);
            SAMPLER(sampler_NprIdTexture);

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);
            float4 _SourceTex_TexelSize;

            int _UseOcclusionCulling;
            StructuredBuffer<uint> _BboxVisibilityFlags;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            // each quad is made of 2 triangles based on the rect of the instance data
            float2 GetQuadUV(uint vertexID)
            {
                switch (vertexID)
                {
                    case 0: 
                        return float2(0, 0);
                    case 1: 
                        return float2(1, 0);
                    case 2: 
                        return float2(1, 1);
                    case 3: 
                        return float2(0, 0);
                    case 4: 
                        return float2(1, 1);
                    case 5: 
                        return float2(0, 1);


                    default:
                        return float2(0, 0); // should never happen
                }
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;

                if (_UseOcclusionCulling != 0)
                {
                    uint bboxIndex = (uint)_InstanceData[input.instanceID].index;
                    uint visible = _BboxVisibilityFlags[bboxIndex];

                    if (visible != 0)
                    {
                        output.posCS = float4(-2, -2, 0, 1);
                        return output;
                    }
                }

                float2 uv = GetQuadUV(input.vertexID);
                float4 rect = _InstanceData[input.instanceID].rect;

                // map local quad UV to pixel coords within bbox
                float2 pixelPos = rect.xy + uv * rect.zw;

                // convert to clip space
                float2 ndc;
                ndc.x = pixelPos.x * _NprScreenSize.z * 2.0 - 1.0;
                ndc.y = 1.0 - pixelPos.y * _NprScreenSize.w * 2.0; // flip y for Unity's screen space

                output.posCS = float4(ndc, 0.0, 1.0);

                // convert pixel coords to texture UVs
                output.screenUV = pixelPos * _NprScreenSize.zw;

                return output;
            }

            uint ReadMask8(float2 uv)
            {
                float m = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_NprIdTexture, uv).r;
                return (uint)round(saturate(m) * 255.0);
            }

            static const uint Bayer8x8[8 * 8] =
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

            float4 Frag(Varyings i) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, i.screenUV);
                uint mask = ReadMask8(i.screenUV);

                const uint DITHERING_BIT = 1u << 1;
                if ((mask & DITHERING_BIT) == 0u)
                    return col;

                uint2 pixelXY = (uint2)(i.screenUV * _SourceTex_TexelSize.zw);
                pixelXY = pixelXY % 8;
                uint idx = pixelXY.y * 8 + pixelXY.x;

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