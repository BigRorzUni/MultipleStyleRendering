Shader "Custom/ScreenspaceOutlines"
{
    Properties
    {
        _OutlineColour ("Outline Colour", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ScreenspaceOutlines"

            ZTest Always 
            ZWrite Off 
            Cull Off 
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NprDepthTexture);
            TEXTURE2D(_NprNormalsTexture);
            TEXTURE2D(_NprIdTexture);
            TEXTURE2D(_NprSourceTexture);
            float4 _NprSourceTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColour;
            CBUFFER_END

            float _ThicknessPx;    

            float _DepthThreshold;   
            float _DepthStrength;    

            float _NormalThreshold; 
            float _NormalStrength;

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

            float3 getNormal(float2 uv)
            {
                float4 norm = SAMPLE_TEXTURE2D(_NprNormalsTexture, sampler_PointClamp, uv);
                return norm * 2.0 - 1.0;
            }
            // get linear depth from raw depth to perform operations on
            float getDepth(float2 uv)
            {
                float raw = SAMPLE_TEXTURE2D(_NprDepthTexture, sampler_PointClamp, uv).r;
                return Linear01Depth(raw, _ZBufferParams);
            }

            uint ReadMask8(float2 uv)
            {
                float r = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_PointClamp, uv).r;
                return (uint)round(saturate(r) * 255.0);
            }

            float4 Frag (Varyings i) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_NprSourceTexture, sampler_PointClamp, i.uv);

                // discard if pixel is not tagged for outlining in id tex
                uint mask = ReadMask8(i.uv);
                const uint SS_OUTLINE_BIT = 1u << 0;
                if ((mask & SS_OUTLINE_BIT) == 0u)
                    return col;
        
                // step size
                float2 stepUV = _NprSourceTexture_TexelSize.xy * max(1.0, _ThicknessPx);

                // dont step onto fullscreen borders
                if (i.uv.x < stepUV.x || i.uv.x > 1.0 - stepUV.x ||
                    i.uv.y < stepUV.y || i.uv.y > 1.0 - stepUV.y)
                    return col;

                float zC = getDepth(i.uv);

                // skip skybox
                if (zC >= 0.999)
                    return col;

                // depth laplacian 
                float zR = getDepth(i.uv + float2( stepUV.x, 0));
                float zL = getDepth(i.uv + float2(-stepUV.x, 0));
                float zU = getDepth(i.uv + float2(0,  stepUV.y));
                float zD = getDepth(i.uv + float2(0, -stepUV.y));

                float lap = abs(zR + zL + zU + zD - 4.0 * zC);
                float lapN = lap / max(zC, 1e-3);
                float depthEdge = lapN * _DepthStrength;
                float depthMask = step(_DepthThreshold, depthEdge);

                // normal discontinuity
                float3 nC = getNormal(i.uv);
                float3 nR = getNormal(i.uv + float2( stepUV.x, 0));
                float3 nL = getNormal(i.uv + float2(-stepUV.x, 0));
                float3 nU = getNormal(i.uv + float2(0,  stepUV.y));
                float3 nD = getNormal(i.uv + float2(0, -stepUV.y));

                float normalEdge = 0.0;
                normalEdge = max(normalEdge, 1.0 - saturate(dot(nC, nR)));
                normalEdge = max(normalEdge, 1.0 - saturate(dot(nC, nL)));
                normalEdge = max(normalEdge, 1.0 - saturate(dot(nC, nU)));
                normalEdge = max(normalEdge, 1.0 - saturate(dot(nC, nD)));
                normalEdge *= _NormalStrength;

                float normalMask = step(_NormalThreshold, normalEdge);

                float edgeMask = max(depthMask, normalMask);

                // outline colour on the outlines, otherwise return src
                return lerp(col, _OutlineColour, edgeMask);

            }
            ENDHLSL
        }
    }
}