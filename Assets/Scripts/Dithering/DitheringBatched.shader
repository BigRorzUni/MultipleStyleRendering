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


            StructuredBuffer<float4> _InstanceData;
            StructuredBuffer<uint> _BboxVisibilityFlags;
            StructuredBuffer<uint> _BboxIndices;
            StructuredBuffer<uint> _BBoxMasks;
            
            float4 _NprScreenSize; 
            int _UseOcclusion;
            int _UseBboxIndices;
            uint _DitheringBit;

            TEXTURE2D(_NprIdTexture);
            SAMPLER(sampler_NprIdTexture);

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);
            float4 _SourceTex_TexelSize;

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

                uint bboxIndex = input.instanceID;
                if (_UseBboxIndices != 0)
                    bboxIndex = _BboxIndices[input.instanceID];

                uint bboxMask = _BBoxMasks[bboxIndex];

                // if this bbox does not have the dithering bit then collapse it
                if ((bboxMask & _DitheringBit) == 0u)
                {
                    output.posCS = float4(-2.0, -2.0, 0.0, 1.0);
                    output.screenUV = float2(0.0, 0.0);
                    return output;
                }

                if (_UseOcclusion != 0)
                {
                    uint visible = _BboxVisibilityFlags[bboxIndex];

                    // visible (1) -> draw
                    // hidden  (0) -> collapse (skip rasterisation)
                    if (visible == 0)
                    {
                        output.posCS = float4(-2.0, -2.0, 0.0, 1.0);
                        output.screenUV = float2(0.0, 0.0);
                        return output;
                    }
                }

                float2 uv = GetQuadUV(input.vertexID);
                float4 rect = _InstanceData[input.instanceID];

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

            uint ReadMask32(float2 uv)
            {
                float4 s = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_PointClamp, uv);

                uint r = (uint)round(saturate(s.r) * 255.0);
                uint g = (uint)round(saturate(s.g) * 255.0);
                uint b = (uint)round(saturate(s.b) * 255.0);
                uint a = (uint)round(saturate(s.a) * 255.0);

                return r | (g << 8) | (b << 16) | (a << 24);
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
                // get colour over bbox texture
                float4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, i.screenUV);
                uint mask = ReadMask32(i.screenUV);

                // if pixels aren't tagged for dithering then leave them unchanged
                if ((mask & _DitheringBit) == 0u)
                    clip(-1);

                uint2 pixelXY = (uint2)(i.screenUV * _SourceTex_TexelSize.zw);

                // flatten pixelXY
                pixelXY = pixelXY % 8;
                uint idx = pixelXY.y * 8 + pixelXY.x;

                // get bayer brightness using the matrix
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