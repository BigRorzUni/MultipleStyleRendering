Shader "Custom/Pixelisation"
{
    Properties
    {

    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Pixelisation"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            TEXTURE2D(_NprIdTexture);
            TEXTURE2D(_NprDepthTexture);
            TEXTURE2D(_SourceTex);
<<<<<<< Updated upstream
            SAMPLER(sampler_SourceTex);
=======
            float4 _SourceTex_TexelSize;
>>>>>>> Stashed changes


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

            uint ReadMask(float2 uv)
            {
                float m = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_PointClamp, uv).r;
                return (uint)round(saturate(m) * 255.0);
            }

            // get linear depth from raw depth to perform operations on
            float getDepth(float2 uv)
            {
                float raw = SAMPLE_TEXTURE2D(_NprDepthTexture, sampler_PointClamp, uv).r;
                return Linear01Depth(raw, _ZBufferParams);
            }

            float2 BlockOrigin(float2 uv, float blockSize)
            {
                float2 res = _ScreenParams.xy;
                float2 pixel = uv * res;

                float2 blockIndex = floor(pixel / blockSize);
                float2 blockOrigin = blockIndex * blockSize;

                // return uv coords
                return blockOrigin / res;
            }

            float4 Frag (Varyings i) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_PointClamp, i.uv);

                const uint PIXELISATION_BIT = 1u << 4;

                float blockSize = 6.0;

                // get centre and size of block for this fragment
                float2 blockOriginUV = BlockOrigin(i.uv, blockSize);
<<<<<<< Updated upstream
                float2 blockSizeUV = float2(blockSize, blockSize) / _ScreenParams.xy;
=======
                float2 blockSizeUV = float2(blockSize, blockSize) * texel;
>>>>>>> Stashed changes

                // TODO: look into bilinear downsampling but for now this is ok
                // https://bartwronski.com/2021/02/15/bilinear-down-upsampling-pixel-grids-and-that-half-pixel-offset/
                float2 points[5];
                points[0] = blockOriginUV + blockSizeUV * 0.5; // centre
                points[1] = blockOriginUV; // bottom left
                points[2] = blockOriginUV + float2(blockSizeUV.x, 0); // bottom right
                points[3] = blockOriginUV + float2(0, blockSizeUV.y); // top left
                points[4] = blockOriginUV + blockSizeUV; // top right

                // if points of the pixel are in the mask then average their colours for pixcolour
                float4 sumCol = 0;
                int count = 0;
                float zTaggedMin = 1.0;

                [unroll] for (int j = 0; j < 5; j++)
                {
                    uint m = ReadMask(points[j]);
                    if((m & PIXELISATION_BIT) != 0u)
                    {
                        sumCol += SAMPLE_TEXTURE2D(_SourceTex, sampler_PointClamp, points[j]);
                        count++;

                        float zPoint = getDepth(points[j]);
                        zTaggedMin = min(zTaggedMin, zPoint);
                    }
                }

                // if no point in block overlaps mask then skip pixel
                if(count == 0)
                    return col;

                // get current frag depth
                float zI = getDepth(i.uv);

                // if current frag is in front of the pixelised object then do not pixelise over it
                if (zI < zTaggedMin)
                    return col;

                // otherwise apply the average colour of the block
                return sumCol / count;
            }
            ENDHLSL
        }
    }
}