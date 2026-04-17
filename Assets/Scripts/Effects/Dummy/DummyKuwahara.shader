Shader "Custom/DummyKuwahara"
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
            Name "DummyKuwahara"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NprIdTexture);

            TEXTURE2D(_SourceTex);

            uint _RequiredBit;

            StructuredBuffer<uint> _BboxVisibilityFlags;
            int _UseOcclusion;
            int _CurrentBboxIndex;

            int _Radius;
            float4 _SourceTex_TexelSize;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.posCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
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

            float4 Frag(Varyings i) : SV_Target
            {
                if (_UseOcclusion != 0)
                {
                    uint visible = _BboxVisibilityFlags[_CurrentBboxIndex];
                    if (visible == 0)
                        clip(-1);
                }

                uint mask = ReadMask32(i.uv);

                if ((mask & _RequiredBit) == 0u)
                    clip(-1);

                int r = max(_Radius, 1);

                float3 mean0, mean1, mean2, mean3;
                float var0, var1, var2, var3;

                // top-left
                SampleRegion(i.uv, -r, 0, -r, 0, mean0, var0);

                // top-right
                SampleRegion(i.uv, 0, r, -r, 0, mean1, var1);

                // bottom-left
                SampleRegion(i.uv, -r, 0, 0, r, mean2, var2);

                // bottom-right
                SampleRegion(i.uv, 0, r, 0, r, mean3, var3);

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