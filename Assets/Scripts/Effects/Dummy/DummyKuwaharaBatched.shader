Shader "Custom/DummyKuwaharaBatched"
{
    Properties
    {
        _SourceTex ("Source", 2D) = "white" {}
        _Radius ("Radius", Int) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "DummyKuwaharaBatched"
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
            TEXTURE2D(_SourceTex);

            uint _RequiredBit;
            int _Radius;
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

            void SampleRegion(
                float2 uv,
                int xMin, int xMax,
                int yMin, int yMax,
                out float3 mean,
                out float variance)
            {
                float2 texel = _SourceTex_TexelSize.xy;

                float3 sum = 0.0;
                float3 sumSq = 0.0;
                float count = 0.0;

                [loop]
                for (int y = yMin; y <= yMax; y++)
                {
                    [loop]
                    for (int x = xMin; x <= xMax; x++)
                    {
                        float2 sampleUv = uv + float2(x, y) * texel;
                        float3 c = SAMPLE_TEXTURE2D(_SourceTex, sampler_PointClamp, sampleUv).rgb;

                        sum += c;
                        sumSq += c * c;
                        count += 1.0;
                    }
                }

                mean = sum / max(count, 1.0);

                float3 var3 = (sumSq / max(count, 1.0)) - (mean * mean);
                variance = var3.r + var3.g + var3.b;
            }

            float4 Frag (Varyings i) : SV_Target
            {
                uint mask = ReadMask32(i.screenUV);

                // only shade pixels belonging to this effect
                if ((mask & _RequiredBit) == 0u)
                    clip(-1);

                int r = max(_Radius, 1);

                float3 mean0, mean1, mean2, mean3;
                float var0, var1, var2, var3;

                // top-left
                SampleRegion(i.screenUV, -r, 0, -r, 0, mean0, var0);

                // top-right
                SampleRegion(i.screenUV, 0, r, -r, 0, mean1, var1);

                // bottom-left
                SampleRegion(i.screenUV, -r, 0, 0, r, mean2, var2);

                // bottom-right
                SampleRegion(i.screenUV, 0, r, 0, r, mean3, var3);

                float3 outCol = mean0;
                float minVar = var0;

                if (var1 < minVar)
                {
                    minVar = var1;
                    outCol = mean1;
                }

                if (var2 < minVar)
                {
                    minVar = var2;
                    outCol = mean2;
                }

                if (var3 < minVar)
                {
                    minVar = var3;
                    outCol = mean3;
                }

                return float4(outCol, 1.0);
            }
            ENDHLSL
        }
    }
}