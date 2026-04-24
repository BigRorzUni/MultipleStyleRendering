Shader "Custom/DummyBatched"
{
    Properties
    {
        _SourceTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "DummyBatched"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
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

            TEXTURE2D(_NprIdTexture);
            SAMPLER(sampler_NprIdTexture);

            uint _RequiredBit;

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

            Varyings Vert (Attributes v)
            {
                Varyings o;

                uint bboxIndex = v.instanceID;
                if (_UseBboxIndices != 0)
                    bboxIndex = _BboxIndices[v.instanceID];

                uint bboxMask = _BBoxMasks[bboxIndex];

                if ((bboxMask & _RequiredBit) == 0u)
                {
                    o.posCS = float4(-2.0, -2.0, 0.0, 1.0);
                    o.screenUV = float2(0.0, 0.0);
                    return o;
                }

                if (_UseOcclusion != 0)
                {
                    uint visible = _BboxVisibilityFlags[bboxIndex];

                    // visible (1) -> draw
                    // hidden  (0) -> collapse (skip rasterisation)
                    if (visible == 0)
                    {
                        o.posCS = float4(-2.0, -2.0, 0.0, 1.0);
                        o.screenUV = float2(0.0, 0.0);
                        return o;
                    }
                }

                float2 uv = GetQuadUV(v.vertexID);
                float4 rect = _InstanceData[v.instanceID];

                // map local quad UV to pixel coords within bbox
                float2 pixelPos = rect.xy + uv * rect.zw;

                // convert to clip space
                float2 ndc;
                ndc.x = pixelPos.x * _NprScreenSize.z * 2.0 - 1.0;
                ndc.y = 1.0 - pixelPos.y * _NprScreenSize.w * 2.0; // flip y for Unity's screen space

                o.posCS = float4(ndc, 0.0, 1.0);

                // convert pixel coords to texture UVs
                o.screenUV = pixelPos * _NprScreenSize.zw;

                return o;
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

            // copilot generated function
            uint HashUint(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

            // copilot generated function
            float3 HashColour(uint x)
            {
                uint h1 = HashUint(x);
                uint h2 = HashUint(x ^ 0x68bc21ebu);
                uint h3 = HashUint(x ^ 0x02e5be93u);

                return float3(
                    (h1 & 255u) / 255.0,
                    (h2 & 255u) / 255.0,
                    (h3 & 255u) / 255.0
                );
            }

            float4 Frag (Varyings i) : SV_Target
            {
                uint mask = ReadMask32(i.screenUV);

                // comment this out to debug occlusion (reverse occlusion check and occluded object will write to their bbox)
                if ((mask & _RequiredBit) == 0u)
                    clip(-1);

                float3 debugCol = HashColour(mask); // only shows the last applied effect
                return float4(debugCol, 1.0);
            }
            ENDHLSL
        }
    }
}