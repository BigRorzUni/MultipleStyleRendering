Shader "Custom/Pixelisation"
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
            Name "Pixelisation"
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

            uint ReadMask(float2 uv)
            {
                float m = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_NprIdTexture, uv).r;
                return (uint)round(saturate(m) * 255.0);
            }

            float2 BlockOrigin(float2 uv, float blockSize)
            {
                float2 res = _SourceTex_TexelSize.zw;
                float2 pixel = uv * res;

                float2 blockIndex = floor(pixel / blockSize);
                float2 blockOrigin = blockIndex * blockSize;

                // return uv coords
                return blockOrigin / res;
            }

            float4 Frag (Varyings i) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, i.uv);

                const uint PIXELISATION_BIT = 1u << 4;

                // float2 texel = _SourceTex_TexelSize.xy;
                float2 res = _SourceTex_TexelSize.zw;
                float2 texel = _SourceTex_TexelSize.xy;   

                // look more into this
                // https://bartwronski.com/2021/02/15/bilinear-down-upsampling-pixel-grids-and-that-half-pixel-offset/
                //float blocksFrac = 0.01; // what fraction of the screen (width) the blocks take up
                float blockSize = 6;  // square size in pixels

                // get centre and size of block for this fragment
                float2 blockOriginUV = BlockOrigin(i.uv, blockSize);
                float2 blockSizeUV = float2(blockSize, blockSize) / res;

                float2 points[5];
                points[0] = blockOriginUV + blockSizeUV * 0.5; // centre
                points[1] = blockOriginUV; // bottom left
                points[2] = blockOriginUV + float2(blockSizeUV.x, 0); // bottom right
                points[3] = blockOriginUV + float2(0, blockSizeUV.y); // top left
                points[4] = blockOriginUV + blockSizeUV; // top right

                // if points of the pixel are in the mask then average their colours for pixcolour
                float4 sumCol = 0;
                int count = 0;
                [unroll] for (int i = 0; i < 5; i++)
                {
                    uint m = ReadMask(points[i]);
                    if((m & PIXELISATION_BIT) != 0u)
                    {
                        sumCol += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, points[i]);
                        count++;
                    }
                }

                // if pixel overlaps mask then return the pixelated colour
                if(count > 0)
                    return sumCol / count;

                // else return the unpixelated colour
                return col;
            }
            ENDHLSL
        }
    }
}